import { Component, OnDestroy, inject, signal } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { form, required, validateTree, FormField } from '@angular/forms/signals';
import { DecimalPipe } from '@angular/common';

interface PipelineResult {
  success: boolean;
  sectionCount: number;
  entryCount: number;
  elapsedSeconds: number;
  errors: string[];
  warnings: string[];
  summaryVersion: string;
  namesVersion: string;
  frameworkVersion: string;
}

interface LogEntry {
  timestamp: string;
  level: string;
  message: string;
}

interface PipelineStatus {
  isRunning: boolean;
  lastRunAt: string | null;
  lastResult: PipelineResult | null;
  currentSummaryVersion: string | null;
  currentNamesVersion: string | null;
  currentFrameworkVersion: string | null;
  builtAt: string | null;
}

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [FormField, DecimalPipe],
  templateUrl: './admin.html',
})
export class AdminComponent implements OnDestroy {
  private http = inject(HttpClient);
  private pollTimer: ReturnType<typeof setInterval> | null = null;

  private authModel = signal({ adminKey: '' });
  authForm = form(this.authModel, (f) => {
    required(f.adminKey);
  });

  private uploadModel = signal({ generateGraph: false });
  uploadForm = form(this.uploadModel, (f) => {
    validateTree(f, () => {
      const hasSummary = !!this.summaryFile();
      const hasNames = !!this.namesFile();
      const hasFramework = !!this.frameworkFile();
      if (hasSummary !== hasNames) {
        return { kind: 'files-mismatch', message: 'If updating summary or names, both files are required.' };
      }
      if (!hasSummary && !hasFramework) {
        return { kind: 'no-files', message: 'Upload both summary and names, or upload the analytical framework by itself.' };
      }
      return null;
    });
  });

  authenticated = signal(false);
  authenticating = signal(false);
  summaryFile = signal<File | null>(null);
  namesFile = signal<File | null>(null);
  frameworkFile = signal<File | null>(null);
  status = signal<PipelineStatus | null>(null);
  result = signal<PipelineResult | null>(null);
  logs = signal<LogEntry[]>([]);
  diagResult = signal<string>('');
  running = signal(false);
  message = signal('');
  error = signal('');

  private headers() {
    return new HttpHeaders({ 'X-Admin-Key': this.authModel().adminKey });
  }

  onSummaryFile(event: Event) {
    const input = event.target as HTMLInputElement;
    this.summaryFile.set(input.files?.[0] ?? null);
  }

  onNamesFile(event: Event) {
    const input = event.target as HTMLInputElement;
    this.namesFile.set(input.files?.[0] ?? null);
  }

  onFrameworkFile(event: Event) {
    const input = event.target as HTMLInputElement;
    this.frameworkFile.set(input.files?.[0] ?? null);
  }

  ngOnDestroy() {
    this.stopPolling();
  }

  testConnectivity() {
    this.diagResult.set('Testing…');
    this.http.get<any>('/proxy-diagnostics').subscribe({
      next: (r) => this.diagResult.set(JSON.stringify(r, null, 2)),
      error: (e) =>
        this.diagResult.set(
          JSON.stringify(e.error ?? { status: e.status, message: e.message }, null, 2),
        ),
    });
  }

  authenticate() {
    if (!this.authModel().adminKey) {
      this.error.set('Enter your admin key.');
      return;
    }
    this.authenticating.set(true);
    this.error.set('');
    this.http.get<PipelineStatus>('/api/admin/status', { headers: this.headers() }).subscribe({
      next: (s) => {
        this.status.set(s);
        this.authenticated.set(true);
        this.authenticating.set(false);
      },
      error: (e) => {
        this.authenticating.set(false);
        this.error.set(
          e.status === 401
            ? 'Invalid admin key.'
            : `Could not reach the API (HTTP ${e.status}). Run the connectivity test before authenticating.`,
        );
      },
    });
  }

  private refreshStatus() {
    this.http.get<PipelineStatus>('/api/admin/status', { headers: this.headers() }).subscribe({
      next: (s) => this.status.set(s),
      error: () => {
        /* best effort */
      },
    });
  }

  upload() {
    if (this.uploadForm().invalid()) return;

    const fd = new FormData();
    if (this.summaryFile()) fd.append('summary', this.summaryFile()!);
    if (this.namesFile()) fd.append('names', this.namesFile()!);
    if (this.frameworkFile()) fd.append('framework', this.frameworkFile()!);
    fd.append('generate_graph', this.uploadModel().generateGraph ? 'true' : 'false');

    this.running.set(true);
    this.result.set(null);
    this.error.set('');
    this.message.set('Pipeline running… this may take a few minutes.');

    this.http.post('/api/admin/upload', fd, { headers: this.headers() }).subscribe({
      next: () => this.startPolling(),
      error: (e) => {
        this.running.set(false);
        this.message.set('');
        if (e.status === 401) {
          this.error.set('Invalid admin key.');
        } else {
          this.error.set('Upload failed.');
        }
      },
    });
  }

  loadLogs() {
    this.http.get<LogEntry[]>('/api/admin/logs', { headers: this.headers() }).subscribe({
      next: (entries) => this.logs.set(entries),
      error: () => {
        /* transient */
      },
    });
  }

  private startPolling() {
    this.pollTimer = setInterval(() => {
      this.http.get<PipelineStatus>('/api/admin/status', { headers: this.headers() }).subscribe({
        next: (s) => {
          this.status.set(s);
          this.loadLogs();
          if (!s.isRunning) {
            this.stopPolling();
            this.running.set(false);
            this.message.set('');
            if (s.lastResult) {
              this.result.set(s.lastResult);
              if (!s.lastResult.success) {
                this.error.set('Pipeline failed. See errors below.');
              }
            }
          }
        },
        error: () => {
          /* transient — keep polling */
        },
      });
    }, 3000);
  }

  private stopPolling() {
    if (this.pollTimer !== null) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }

  cancelPipeline() {
    this.http
      .post<{ message: string }>('/api/admin/cancel', {}, { headers: this.headers() })
      .subscribe({
        next: (r) => this.message.set(r.message),
        error: (e) => this.error.set(e.status === 400 ? 'No pipeline running.' : 'Cancel failed.'),
      });
  }

  rollback() {
    if (!confirm('Roll back to previous database version?')) return;
    this.http
      .post<{ message: string }>('/api/admin/rollback', {}, { headers: this.headers() })
      .subscribe({
        next: (r) => {
          this.message.set(r.message);
          this.refreshStatus();
        },
        error: (e) => this.error.set(e.status === 404 ? 'No backup found.' : 'Rollback failed.'),
      });
  }

  deauthenticate() {
    this.stopPolling();
    this.authenticated.set(false);
    this.authModel.set({ adminKey: '' });
    this.status.set(null);
    this.result.set(null);
    this.logs.set([]);
    this.diagResult.set('');
    this.error.set('');
    this.message.set('');
  }
}

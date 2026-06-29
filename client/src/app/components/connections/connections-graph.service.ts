import * as d3 from 'd3';
import { Service, WritableSignal } from '@angular/core';

export interface GraphNode {
  id: string;
  name: string;
  type: string;
  tier: number;
  tags: string[];
  notes: string;
}

export interface GraphEdge {
  source: string;
  target: string;
  relationship: string;
  evidentiary_weight: string;
  directional: boolean;
}

export interface GraphData {
  nodes: GraphNode[];
  edges: GraphEdge[];
}

export interface SimNode extends d3.SimulationNodeDatum {
  id: string;
  name: string;
  type: string;
  tier: number;
  tags: string[];
  notes: string;
}

export interface SimEdge extends d3.SimulationLinkDatum<SimNode> {
  relationship: string;
  evidentiary_weight: string;
  directional: boolean;
}

export interface VisibilityParams {
  searchQuery: string;
  filterWeight: string;
  filterType: string;
  showTier2: boolean;
  selectedNode: SimNode | null;
  egoMode: boolean;
  pinnedNodes: Set<string>;
}

interface GraphSignals {
  selectedNode: WritableSignal<SimNode | null>;
  egoMode: WritableSignal<boolean>;
  pinnedNodes: WritableSignal<Set<string>>;
}

const EDGE_COLORS: Record<string, string> = {
  CONFIRMED: '#4ade80',
  CORROBORATED: '#60a5fa',
  DOCUMENTED_CLAIM: '#facc15',
  CONFIRMED_GOVT_ACTION: '#f97316',
  ANOMALOUS: '#e879f9',
};

const DEFAULT_ZOOM_SCALE = 0.85;
const LINK_DISTANCE = 110;
const CHARGE_STRENGTH = -260;
const COLLISION_PADDING = 20;
const CENTERING_STRENGTH = 0.03;
const HUB_CENTERING_STRENGTH = 0.35;

@Service()
export class ConnectionsGraphService {
  private simulation!: d3.Simulation<SimNode, SimEdge>;
  private linkSel!: d3.Selection<SVGLineElement, SimEdge, SVGGElement, unknown>;
  private nodeSel!: d3.Selection<SVGGElement, SimNode, SVGGElement, unknown>;
  private zoomBehavior!: d3.ZoomBehavior<SVGSVGElement, unknown>;
  private svgSel!: d3.Selection<SVGSVGElement, unknown, null, undefined>;
  private tooltip!: d3.Selection<HTMLDivElement, unknown, HTMLElement, unknown>;
  private neighborMap = new Map<string, Set<string>>();
  private signals: GraphSignals | null = null;

  initGraph(
    svgEl: SVGSVGElement,
    data: GraphData,
    hubConfig: { id: string; aliases: string[] } | undefined,
    signals: GraphSignals,
  ): void {
    this.signals = signals;
    const { selectedNode, egoMode, pinnedNodes } = signals;

    const width = svgEl.clientWidth || 1200;
    const height = svgEl.clientHeight || 800;

    const simNodes: SimNode[] = data.nodes.map((n) => ({ ...n }));
    const nodeById = new Map(simNodes.map((n) => [n.id, n]));

    const simEdges: SimEdge[] = data.edges
      .filter((e) => nodeById.has(e.source) && nodeById.has(e.target))
      .map((e) => ({
        source: e.source,
        target: e.target,
        relationship: e.relationship,
        evidentiary_weight: e.evidentiary_weight,
        directional: e.directional,
      }));

    this.neighborMap.clear();
    simEdges.forEach((e) => {
      const srcId = e.source as string;
      const tgtId = e.target as string;
      if (!this.neighborMap.has(srcId)) this.neighborMap.set(srcId, new Set());
      if (!this.neighborMap.has(tgtId)) this.neighborMap.set(tgtId, new Set());
      this.neighborMap.get(srcId)!.add(tgtId);
      this.neighborMap.get(tgtId)!.add(srcId);
    });

    const degreeMap = new Map<string, number>(simNodes.map((n) => [n.id, 0]));
    simEdges.forEach((e) => {
      degreeMap.set(e.source as string, (degreeMap.get(e.source as string) ?? 0) + 1);
      degreeMap.set(e.target as string, (degreeMap.get(e.target as string) ?? 0) + 1);
    });

    const nodeSize = (n: SimNode) => (n.tier === 1 ? 300 : 150) + (degreeMap.get(n.id) ?? 0) * 60;
    const nodeRadius = (n: SimNode) => Math.sqrt(nodeSize(n) / Math.PI) * 1.5 + 5;
    const collisionRadius = (n: SimNode) => nodeRadius(n) + COLLISION_PADDING;
    const primaryHubNode = this.findPrimaryHubNode(simNodes, degreeMap, hubConfig);

    if (primaryHubNode) {
      primaryHubNode.x = width / 2;
      primaryHubNode.y = height / 2;
    }

    const symbolTypeMap: Record<string, d3.SymbolType> = {
      person: d3.symbolCircle,
      institution: d3.symbolSquare,
      location: d3.symbolDiamond,
      event: d3.symbolStar,
      fund: d3.symbolTriangle,
    };

    this.svgSel = d3.select(svgEl);
    this.svgSel.selectAll('*').remove();

    const defs = this.svgSel.append('defs');
    Object.entries(EDGE_COLORS).forEach(([weight, color]) => {
      defs
        .append('marker')
        .attr('id', `arrow-${weight}`)
        .attr('viewBox', '0 -5 10 10')
        .attr('refX', 18)
        .attr('refY', 0)
        .attr('markerWidth', 6)
        .attr('markerHeight', 6)
        .attr('orient', 'auto')
        .append('path')
        .attr('d', 'M0,-5L10,0L0,5')
        .attr('fill', color)
        .attr('opacity', 0.8);
    });

    this.zoomBehavior = d3
      .zoom<SVGSVGElement, unknown>()
      .scaleExtent([0.05, 8])
      .on('zoom', (event) => mainG.attr('transform', event.transform));

    this.svgSel.call(this.zoomBehavior).on('click', () => {
      selectedNode.set(null);
      egoMode.set(false);
    });

    const mainG = this.svgSel.append('g').attr('class', 'main-g');
    this.svgSel.call(this.zoomBehavior.transform, this.getDefaultZoomTransform());

    this.tooltip = d3.select(document.body).append('div') as unknown as d3.Selection<
      HTMLDivElement,
      unknown,
      HTMLElement,
      unknown
    >;
    this.tooltip.attr('class', 'graph-tooltip').style('display', 'none');

    this.linkSel = mainG
      .append('g')
      .attr('class', 'links')
      .selectAll<SVGLineElement, SimEdge>('line')
      .data(simEdges)
      .join('line')
      .attr('stroke', (d) => EDGE_COLORS[d.evidentiary_weight] ?? '#64748b')
      .attr('stroke-width', 1.5)
      .attr('stroke-opacity', 0.6)
      .attr('marker-end', (d) => (d.directional ? `url(#arrow-${d.evidentiary_weight})` : null));

    this.linkSel
      .on('mouseover', (event, d) => {
        this.tooltip
          .style('display', 'block')
          .html(
            `<div class="tt-rel">${d.relationship}</div><div class="tt-badge">${d.evidentiary_weight.replace(/_/g, ' ')}</div>`,
          );
        this.moveTooltip(event);
      })
      .on('mousemove', (event) => this.moveTooltip(event))
      .on('mouseout', () => this.tooltip.style('display', 'none'));

    let dragWithModifier = false;
    const drag = d3
      .drag<SVGGElement, SimNode>()
      .on('start', (event, d) => {
        if (!event.active) this.simulation.alphaTarget(0.3).restart();
        d.fx = d.x;
        d.fy = d.y;
        dragWithModifier = event.sourceEvent.ctrlKey || event.sourceEvent.metaKey;
      })
      .on('drag', (event, d) => {
        d.fx = event.x;
        d.fy = event.y;
      })
      .on('end', (event, d) => {
        if (!event.active) this.simulation.alphaTarget(0);
        const isPinningDrag =
          dragWithModifier || event.sourceEvent.ctrlKey || event.sourceEvent.metaKey;
        dragWithModifier = false;
        const pinned = pinnedNodes();
        if (isPinningDrag) {
          if (pinned.has(d.id)) {
            pinned.delete(d.id);
            d.fx = null;
            d.fy = null;
          } else {
            pinned.add(d.id);
          }
          pinnedNodes.set(new Set(pinned));
        } else {
          if (!pinned.has(d.id)) {
            d.fx = null;
            d.fy = null;
          }
        }
      });

    this.nodeSel = mainG
      .append('g')
      .attr('class', 'nodes')
      .selectAll<SVGGElement, SimNode>('g')
      .data(simNodes)
      .join('g')
      .attr('class', 'node-group')
      .style('cursor', 'pointer')
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      .call(drag as any);

    this.nodeSel
      .append('path')
      .attr('d', (d) => d3.symbol(symbolTypeMap[d.type] ?? d3.symbolCircle, nodeSize(d))())
      .attr('fill', '#1e293b')
      .attr('stroke', (d) => (d.tier === 1 ? '#94a3b8' : '#475569'))
      .attr('stroke-width', (d) => (d.tier === 1 ? 1.5 : 1));

    this.nodeSel
      .append('text')
      .text((d) => d.name)
      .attr('text-anchor', 'middle')
      .attr('dy', (d) => nodeRadius(d) + 4)
      .attr('fill', (d) => (d.tier === 1 ? '#cbd5e1' : '#64748b'))
      .attr('font-size', (d) => (d.tier === 1 ? '11px' : '9px'))
      .style('pointer-events', 'none')
      .style('user-select', 'none');

    this.nodeSel
      .on('mouseover', (event, d) => {
        const tags = d.tags.map((t) => `<span class="tt-badge">${t}</span>`).join('');
        this.tooltip.style('display', 'block').html(`
            <div class="tt-name">${d.name}</div>
            <div class="tt-sub">${d.type} · Tier ${d.tier}</div>
            ${tags ? `<div style="margin-top:4px">${tags}</div>` : ''}
            ${d.notes ? `<div class="tt-sep"></div><div class="tt-notes">${d.notes}</div>` : ''}
          `);
        this.moveTooltip(event);
      })
      .on('mousemove', (event) => this.moveTooltip(event))
      .on('mouseout', () => this.tooltip.style('display', 'none'))
      .on('click', (event, d) => {
        event.stopPropagation();
        const current = selectedNode();
        if (current?.id === d.id) {
          egoMode.set(!egoMode());
        } else {
          selectedNode.set(d);
          egoMode.set(false);
        }
      })
      .on('dblclick', (event) => {
        event.stopPropagation();
        selectedNode.set(null);
        egoMode.set(false);
      });

    this.simulation = d3
      .forceSimulation<SimNode>(simNodes)
      .force(
        'link',
        d3
          .forceLink<SimNode, SimEdge>(simEdges)
          .id((d) => d.id)
          .distance(LINK_DISTANCE)
          .strength(0.2),
      )
      .force('charge', d3.forceManyBody().strength(CHARGE_STRENGTH))
      .force('center', d3.forceCenter(width / 2, height / 2))
      .force(
        'x',
        d3
          .forceX<SimNode>(width / 2)
          .strength((d) =>
            d.id === primaryHubNode?.id ? HUB_CENTERING_STRENGTH : CENTERING_STRENGTH,
          ),
      )
      .force(
        'y',
        d3
          .forceY<SimNode>(height / 2)
          .strength((d) =>
            d.id === primaryHubNode?.id ? HUB_CENTERING_STRENGTH : CENTERING_STRENGTH,
          ),
      )
      .force('collide', d3.forceCollide<SimNode>().radius(collisionRadius).strength(0.9))
      .on('tick', () => {
        this.linkSel
          .attr('x1', (d) => (d.source as SimNode).x ?? 0)
          .attr('y1', (d) => (d.source as SimNode).y ?? 0)
          .attr('x2', (d) => (d.target as SimNode).x ?? 0)
          .attr('y2', (d) => (d.target as SimNode).y ?? 0);
        this.nodeSel.attr('transform', (d) => `translate(${d.x ?? 0},${d.y ?? 0})`);
      });
  }

  applyVisibility(params: VisibilityParams): void {
    if (!this.linkSel || !this.nodeSel) return;

    const { searchQuery, filterWeight, filterType, showTier2, selectedNode, egoMode, pinnedNodes } =
      params;
    const query = searchQuery.toLowerCase().trim();
    const neighbors = selectedNode
      ? (this.neighborMap.get(selectedNode.id) ?? new Set<string>())
      : null;

    this.nodeSel.attr('class', (d) => `node-group ${pinnedNodes.has(d.id) ? 'pinned' : ''}`);

    const nodeHidden = (d: SimNode): boolean => {
      if (!showTier2 && d.tier === 2) return true;
      if (filterType && d.type !== filterType) return true;
      if (selectedNode && egoMode && d.id !== selectedNode.id && !neighbors!.has(d.id)) return true;
      return false;
    };

    this.nodeSel
      .style('display', (d) => (nodeHidden(d) ? 'none' : null))
      .style('opacity', (d) => {
        if (nodeHidden(d)) return null;
        if (selectedNode) {
          if (d.id === selectedNode.id || neighbors!.has(d.id)) return 1;
          return 0.06;
        }
        if (query && !d.name.toLowerCase().includes(query)) return 0.15;
        return 1;
      });

    this.linkSel
      .style('display', (d: SimEdge) => {
        const src = d.source as SimNode;
        const tgt = d.target as SimNode;
        if (nodeHidden(src) || nodeHidden(tgt)) return 'none';
        if (filterWeight && d.evidentiary_weight !== filterWeight) return 'none';
        if (selectedNode && egoMode && src.id !== selectedNode.id && tgt.id !== selectedNode.id)
          return 'none';
        return null;
      })
      .style('opacity', (d: SimEdge) => {
        const src = d.source as SimNode;
        const tgt = d.target as SimNode;
        if (selectedNode) {
          if (src.id === selectedNode.id || tgt.id === selectedNode.id) return 0.8;
          return 0.06;
        }
        return 0.6;
      });
  }

  resetZoom(): void {
    if (this.svgSel && this.zoomBehavior) {
      this.svgSel
        .transition()
        .duration(500)
        .call(this.zoomBehavior.transform, this.getDefaultZoomTransform());
    }
  }

  destroy(): void {
    if (this.simulation) this.simulation.stop();
    if (this.tooltip) this.tooltip.remove();
    this.signals = null;
  }

  private findPrimaryHubNode(
    nodes: SimNode[],
    degreeMap: Map<string, number>,
    hubConfig: { id: string; aliases: string[] } | undefined,
  ): SimNode | null {
    const hubId = hubConfig?.id ?? '';
    const aliases = hubConfig?.aliases ?? [];
    return (
      nodes.find((n) => n.id === hubId) ??
      (aliases.length > 0
        ? nodes.find((n) => aliases.some((a) => n.name.toLowerCase().includes(a.toLowerCase())))
        : null) ??
      nodes.reduce<SimNode | null>((hub, node) => {
        if (!hub) return node;
        return (degreeMap.get(node.id) ?? 0) > (degreeMap.get(hub.id) ?? 0) ? node : hub;
      }, null)
    );
  }

  private getDefaultZoomTransform(): d3.ZoomTransform {
    return d3.zoomIdentity.scale(DEFAULT_ZOOM_SCALE);
  }

  private moveTooltip(event: MouseEvent): void {
    this.tooltip.style('left', `${event.pageX + 12}px`).style('top', `${event.pageY - 28}px`);
  }
}

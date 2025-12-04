# Frontend AG-UI Integration Design

## Goals
- Align the existing React frontend with the AG-UI protocol using the `@ag-ui/client` (and future `@copilotkit/react` migration) for agent runs.
- Support child lifecycle: create child, view profile, add wishlist (toy ideas), view recommendations, trigger logistics assessment, generate report.
- Provide real-time, streaming agent interaction (run status + reasoning deltas) integrated with Microsoft Agent Framework back-end abstractions and Drasi event graph updates.
- Establish an extensible event mapping layer so back-end SSE events can evolve toward full AG-UI compliance without breaking UI.

## Current State
| Area | Status |
|------|--------|
| Agent runs | Implemented via `/agents/{agentId}/run` SSE endpoints (custom) emitting simplified AG-UI-like events. |
| Cancellation | `/agents/{agentId}/cancel` implemented. |
| Child creation | `/children` POST implemented. |
| Child profile | `/children/{childId}/profile` returns placeholder profile. |
| Recommendations | `/children/{childId}/recommendations` returns stub set. |
| Logistics assessment | `/children/{childId}/logistics` exists (not yet wired into UI). |
| Wishlist events | Publisher exists (`EventHubPublisher.PublishWishlistAsync`), but no public endpoint nor UI form. |
| Drasi real-time updates | Graph defined (`drasi/graph.yaml`), but no front-end subscription path yet. |
| Report retrieval | `/reports/{childId}` used post-run; not surfaced beyond raw metadata UI. |

## Proposed Additions
1. Wishlist (Toy Idea) Endpoint: `POST /children/{childId}/wishlist` -> invokes `IEventPublisher.PublishWishlistAsync` with payload containing a toy idea (name, category, optional price ceiling). Returns 202 and dedupe key.
2. Drasi Event Stream Endpoint: `GET /stream/children/{childId}` (SSE or WebSocket) -> pushes incremental recommendation updates or assessment results when graph recomputes.
3. Frontend State Context: `AGUIProvider` wrapping app; supplies `agentClient`, `childStore`, `wishlistActions`, `recommendationStore`.
4. Event Mapping Layer: Adapter translating backend SSE events `{ type: RUN_STARTED, TEXT_MESSAGE_CONTENT, RUN_FINISHED }` into richer AG-UI canonical shape consumed by `useAgentRun(agentId)` hook.
5. React Pages:
   - `ChildrenDashboardPage`: list/search children, link to detail.
   - `ChildDetailPage` (rename `ChildRunAgentPage`): tabs: Profile | Wishlist | Recommendations | Logistics | Reports | Agent Run.
   - `AddWishlistItem` component within Wishlist tab.
   - `ElfAgentsStatusPage`: surfaces `/elf-agents/status` and readiness, with aggregated metrics + roles.
6. Progressive Enhancement: If `@copilotkit/react` becomes primary, swap adapter but keep internal hook contracts stable.

## AG-UI Event Normalization
Backend currently emits: `RUN_STARTED`, `TEXT_MESSAGE_START`, `TEXT_MESSAGE_CONTENT`, `TEXT_MESSAGE_END`, `RUN_FINISHED`.

Target normalized shape (sample):
```ts
interface NormalizedRunEvent {
  phase: 'run' | 'message';
  kind: 'started' | 'delta' | 'end' | 'finished';
  runId: string;
  threadId?: string;
  messageId?: string;
  deltaText?: string;
  status?: 'succeeded' | 'cancelled' | 'error';
}
```

Mapping Table:
| Raw Type | Normalized |
|----------|------------|
| RUN_STARTED | `{ phase:'run', kind:'started', runId, threadId }` |
| TEXT_MESSAGE_START | `{ phase:'message', kind:'started', messageId }` |
| TEXT_MESSAGE_CONTENT | `{ phase:'message', kind:'delta', messageId, deltaText: delta }` |
| TEXT_MESSAGE_END | `{ phase:'message', kind:'end', messageId }` |
| RUN_FINISHED | `{ phase:'run', kind:'finished', runId, status: result.status }` |

## React Hook Design
```ts
// useAgentRun.ts
function useAgentRun(agentId: string) {
  const [events, setEvents] = useState<NormalizedRunEvent[]>([]);
  const [status, setStatus] = useState<'idle'|'running'|'finished'|'cancelled'>('idle');
  const start = useCallback(() => { /* open SSE; push events via mapper */ }, [agentId]);
  const cancel = useCallback(() => { /* call cancel endpoint */ }, [agentId]);
  const transcript = useMemo(() => events.filter(e=>e.phase==='message').map(e=>e.deltaText).join(''), [events]);
  return { start, cancel, status, events, transcript };
}
```

## Wishlist Flow
1. User opens Child Detail -> Wishlist tab.
2. Form `Toy name`, `Category`, `Notes`, optional `Budget limit`.
3. `POST /children/{childId}/wishlist` body:
```json
{ "toyName": "Lego Space Cruiser", "category": "STEM", "notes": "Prefers space theme", "budgetLimit": 80 }
```
4. Backend constructs wishlist JSON node; calls `PublishWishlistAsync(childId, dedupeKey, schemaVersion, wishlist)`.
5. EventHub event consumed by Drasi -> graph recomputes -> new recommendation set persisted.
6. Frontend Drasi subscription receives `recommendation-update` events -> updates recommendation list live.

## Drasi Subscription (Placeholder)
```ts
function useChildRecommendationsLive(childId: string) {
  const [recs, setRecs] = useState<Recommendation[]>([]);
  useEffect(()=>{
    const es = new EventSource(`${API_URL}/stream/children/${childId}`);
    es.onmessage = (e)=>{ const data = JSON.parse(e.data); if(data.type==='recommendation-update') setRecs(data.recommendations); };
    return ()=> es.close();
  }, [childId]);
  return recs;
}
```

## UI Component Layout
```
<App>
  <AGUIProvider>
    <TopNav />
    <Routes>
      / -> <ChildrenDashboardPage />
      /children/:id -> <ChildDetailPage /> (tabs)
      /elf-agents/status -> <ElfAgentsStatusPage />
    </Routes>
  </AGUIProvider>
</App>
```

Child Detail Tabs:
- Profile: static fetch + derived metrics.
- Wishlist: add item form + list of last N items (needs new backend retrieval endpoint in future).
- Recommendations: live list via Drasi subscription; manual refresh fallback.
- Logistics: button to POST `/children/{childId}/logistics`; show assessment.
- Reports: show latest `reportMeta` + link to download artifact.
- Agent Run: uses `useAgentRun` to stream reasoning and generate new report.

## Integration with Microsoft Agent Framework
Although the backend currently simulates runs, design assumes future agent orchestration (workflow, tool calls) will produce structured AG-UI events. The normalization layer isolates UI from shape changes.

Planned additions backend side (not yet implemented here):
- Enhanced run endpoint returning tool invocation events.
- Persistent run history in Cosmos DB for analytics.
- Drasi graph nodes referencing run outcomes for per-child timeline views.

## Migration Strategy to CopilotKit React Client
1. Keep adapter interface (`useAgentRun`) stable.
2. Introduce CopilotKit Provider side-by-side; route SSE events into CopilotKit's state API.
3. Gradually replace custom normalization with native event shapes once backend emits full AG-UI spec.

## Security & Resilience Considerations
- Guard input sanitization for wishlist items (prevent injection into agent prompts).
- Implement exponential backoff + retry for Drasi event stream reconnection.
- AbortController integration in `useAgentRun` to prevent memory leaks on route changes.
- Telemetry hooks (console now; later OpenTelemetry) capturing run durations and error states.

## Incremental Delivery Plan (Frontend)
1. Add normalization adapter + `useAgentRun` hook.
2. Refactor `RunAgent.tsx` to use hook + improved transcript view.
3. Create `ChildrenDashboardPage` listing known children (placeholder via `/children/{childId}` existence or static list until list endpoint added).
4. Rename / unify `ChildRunAgentPage` -> `ChildDetailPage` with tabbed navigation.
5. Add Wishlist tab + form (hook pending backend endpoint).
6. Integrate Drasi live recommendations hook (placeholder SSE until endpoint ready).
7. Add ElfAgentsStatusPage using `/elf-agents/status`.
8. Hardening: backoff, error boundaries.

## Open Backend Gaps
- `POST /children/{childId}/wishlist` endpoint.
- Recommendations live stream endpoint.
- Child list enumeration endpoint.
- Assessment result storage for historical view.

## Conclusion
This design harmonizes current demo endpoints with AG-UI concepts while laying a forward-compatible path toward richer agent interactions, real-time recommendation updates, and CopilotKit adoption without churn in existing UI components.

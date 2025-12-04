// Refactored to use the real AG-UI client HttpAgent instead of local shim
// Lazy load AG-UI client to keep initial bundle smaller.
// Types are treated as 'any' to avoid pulling library into main chunk.

export interface ChildProfile {
  id: string;
  name?: string | null;
  age?: number | null;
  location?: string | null;
  preferences?: string[] | null;
  constraints?: { budget?: number | null } | null;
  behaviorSummary?: string | null;
}

export interface Recommendation {
  id: string;
  childId: string;
  suggestion: string;
  rationale: string;
  price?: number | null;
  budgetFit: string;
}
export interface ReportMeta {
  childId: string;
  path: string;
  createdAt: string;
  label: string;
  topN: number;
}

// API base resolution:
// - If running in Azure Static Web Apps OR Azure Container Apps, use '' (relative /api URLs; same-origin backend).
// - Else if VITE_API_URL explicitly set (by azd prebuild hook), use it (empty string allowed for relative).
// - Else if window.__API_BASE__ injected at runtime, use it.
// - Else fallback to local dev API on http://localhost:8080.
const viteEnv: any = (import.meta as any).env || {};

export const API_URL = (() => {
  // FIRST: If running on Azure Static Web Apps OR Azure Container Apps, ALWAYS use empty string (relative URLs)
  // This avoids stale absolute domains baked at build time. The platform handles /api/* routing to the backend.
  if (typeof window !== "undefined") {
    const host = window.location.hostname || "";
    if (host.includes("azurestaticapps") || host.includes("azurecontainerapps")) {
      return ""; // Empty = relative URLs (/api/* routes)
    }
  }

  // Build-time VITE_API_URL (injected by azd prebuild hook)
  const viteApiUrl = viteEnv.VITE_API_URL;
  if (viteApiUrl !== undefined && viteApiUrl !== null) {
    const trimmed = viteApiUrl.trim().replace(/\/$/, "");
    // Empty string is valid - means use relative URLs
    return trimmed;
  }

  // Runtime injected window.__API_BASE__ (index.html)
  if (typeof window !== "undefined" && (window as any).__API_BASE__) {
    return (window as any).__API_BASE__.toString().replace(/\/$/, "");
  }

  // Development fallback: when running vite dev server without envs, prefer relative URLs
  // and let Vite proxy route /api -> localhost:8080. If no browser context, default to localhost.
  if (typeof window !== "undefined") {
    return "";
  }
  return "http://localhost:8080";
})();

console.log(
  "[agentClient] API_URL resolved to:",
  API_URL || "(empty/relative)"
);
console.log(
  "[agentClient] window.location.hostname:",
  typeof window !== "undefined" ? window.location.hostname : "N/A"
);
console.log("[agentClient] VITE_API_URL:", viteEnv.VITE_API_URL);

// Optional X-Role header for backend tracking/observability (not enforced)
const JSON_HEADERS = {
  "Content-Type": "application/json",
  "X-Role": "operator", // Optional: helps track usage patterns in logs
} as const;

export async function createChild(childId: string): Promise<ChildProfile> {
  const res = await fetch(`${API_URL}/api/v1/children`, {
    method: "POST",
    headers: JSON_HEADERS,
    body: JSON.stringify({ childId }),
  });
  if (!res.ok) throw new Error(`Create child failed: ${res.status}`);
  return res.json();
}

export interface WishlistItemInput {
  toyName: string;
  category?: string;
  notes?: string;
  budgetLimit?: number;
}

export async function addWishlistItem(
  childId: string,
  item: WishlistItemInput
): Promise<{ dedupeKey: string }> {
  // Backend endpoint is /api/v1/children/{childId}/wishlist-items
  // Map frontend field names to backend expectations
  const backendPayload = {
    text: item.toyName,
    category: item.category,
    budgetEstimate: item.budgetLimit,
  };
  
  const res = await fetch(
    `${API_URL}/api/v1/children/${encodeURIComponent(childId)}/wishlist-items`,
    {
      method: "POST",
      headers: JSON_HEADERS,
      body: JSON.stringify(backendPayload),
    }
  );
  if (!res.ok) throw new Error(`Add wishlist item failed: ${res.status}`);
  return res.json();
}

export async function getChildProfile(
  childId: string
): Promise<ChildProfile | null> {
  const res = await fetch(
    `${API_URL}/api/v1/children/${encodeURIComponent(childId)}/profile`,
    { headers: JSON_HEADERS }
  );
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`Get child profile failed: ${res.status}`);
  return res.json();
}

export async function getChildRecommendations(
  childId: string
): Promise<Recommendation[]> {
  const res = await fetch(
    `${API_URL}/api/v1/children/${encodeURIComponent(childId)}/recommendations`,
    { headers: JSON_HEADERS }
  );
  if (!res.ok) throw new Error(`Get recommendations failed: ${res.status}`);
  const raw = await res.json();
  const list: any[] = raw?.items || raw; // backend returns { items, count }
  return (Array.isArray(list) ? list : []).map((r) => ({
    id: r.id || r.Id || crypto.randomUUID(),
    childId: r.childId || r.ChildId || childId,
    suggestion: r.suggestion || r.Suggestion || "",
    rationale: r.rationale || r.Rationale || "",
    price: r.price ?? r.Price ?? null,
    budgetFit: r.budgetFit || r.BudgetFit || "unknown",
  }));
}

export async function runLogisticsAssessment(childId: string): Promise<any> {
  const res = await fetch(
    `${API_URL}/api/v1/children/${encodeURIComponent(childId)}/logistics`,
    { method: "POST", headers: JSON_HEADERS }
  );
  if (!res.ok) throw new Error(`Logistics assessment failed: ${res.status}`);
  return res.json();
}

export async function getReport(childId: string): Promise<ReportMeta | null> {
  const res = await fetch(`${API_URL}/api/v1/reports/${childId}`, {
    headers: JSON_HEADERS,
  });
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`Get report failed: ${res.status}`);
  return res.json();
}

export interface CreateJobRequest {
  childId: string;
  dedupeKey?: string;
  schemaVersion?: string;
  wishlist?: any;
}

export async function createJob(request: CreateJobRequest): Promise<void> {
  const res = await fetch(`${API_URL}/api/v1/jobs`, {
    method: "POST",
    headers: JSON_HEADERS,
    body: JSON.stringify(request),
  });
  if (!res.ok) throw new Error(`Create job failed: ${res.status}`);
}

export interface NotificationDto {
  id: string;
  childId: string;
  type: string;
  message: string;
  createdAt: string;
  state: string;
  relatedRecommendationSetId?: string | null;
}

export async function getNotifications(
  state?: string
): Promise<NotificationDto[]> {
  const url = state
    ? `${API_URL}/api/v1/notifications?state=${encodeURIComponent(state)}`
    : `${API_URL}/api/v1/notifications`;
  const res = await fetch(url, { headers: JSON_HEADERS });
  if (!res.ok) throw new Error(`Get notifications failed: ${res.status}`);
  const raw = await res.json();
  const list: any[] = raw?.items || raw; // backend returns { items, count }
  return (Array.isArray(list) ? list : []).map((n) => ({
    id: n.id || n.Id || crypto.randomUUID(),
    childId: n.childId || n.ChildId || "",
    type: n.type || n.Type || "info",
    message: n.message || n.Message || "",
    createdAt: n.createdAt || n.CreatedAt || new Date().toISOString(),
    state: n.state || n.State || "new",
    relatedRecommendationSetId:
      n.relatedRecommendationSetId || n.RelatedId || null,
  }));
}

export function runAgent(
  agentId: string,
  onEvent: (ev: any) => void,
  onFinished: () => void,
  drasiContext?: any // Optional Drasi real-time context
) {
  let subscription: any;
  let agentInstance: any;
  (async () => {
    const mod = await import("@ag-ui/client");
    const { HttpAgent, EventType } = mod as any;
    const agentRunUrl = (() => {
      const path = `/api/v1/agents/${agentId}/run`;
      if (API_URL && API_URL.length > 0) {
        return `${API_URL}${path}`;
      }
      if (typeof window !== "undefined" && window.location?.origin) {
        return `${window.location.origin}${path}`;
      }
      return path; // fallback; HttpAgent should handle absolute if possible
    })();

    agentInstance = new HttpAgent({
      url: agentRunUrl,
      agentId,
    });

    // Build context array with Drasi insights if available
    const contextArray = [];
    if (drasiContext?.insights) {
      contextArray.push({
        type: "drasi-insights",
        source: "drasi-event-graph",
        timestamp: new Date().toISOString(),
        data: {
          trending: drasiContext.insights.trending?.slice(0, 5) || [],
          duplicates: drasiContext.insights.duplicates || [],
          inactiveChildren: drasiContext.insights.inactiveChildren || [],
          behaviorChanges: drasiContext.insights.behaviorChanges || [],
          stats: drasiContext.insights.stats || {},
        },
      });
    }

    subscription = agentInstance
      .run({
        threadId: crypto.randomUUID(),
        runId: crypto.randomUUID(),
        messages: [],
        tools: [],
        context: contextArray, // Now enriched with Drasi data
        forwardedProps: {},
      })
      .subscribe({
        next: (ev: any) => {
          onEvent(ev);
          if (ev.type === EventType.RUN_FINISHED) {
            onFinished();
            try {
              subscription.unsubscribe();
            } catch {}
          }
        },
        error: (err: any) => {
          // Error is handled by caller via onUpdate callback
          // Browser console will still show network errors for debugging
          try {
            subscription.unsubscribe();
          } catch {}
          onFinished();
        },
        complete: () => {
          onFinished();
        },
      });
  })();
  return {
    close() {
      try {
        agentInstance?.abortRun();
      } catch {}
      try {
        subscription?.unsubscribe();
      } catch {}
    },
  };
}

export function cancelAgentRun(agentId: string): Promise<void> {
  return fetch(`${API_URL}/api/v1/agents/${agentId}/cancel`, {
    method: "DELETE",
  }).then(() => {});
}

// Convenience: start a run with a single user prompt and stream events
export function runAgentWithPrompt(
  agentId: string,
  prompt: string,
  handlers: { onEvent: (ev: any) => void; onFinished: () => void },
  drasiContext?: any // Optional Drasi real-time context
) {
  let subscription: any;
  let agentInstance: any;
  (async () => {
    try {
      const mod = await import("@ag-ui/client");
      const { HttpAgent, EventType } = mod as any;
      const agentRunUrl = (() => {
        const path = `/api/v1/agents/${agentId}/run`;
        if (API_URL && API_URL.length > 0) {
          return `${API_URL}${path}`;
        }
        if (typeof window !== "undefined" && window.location?.origin) {
          return `${window.location.origin}${path}`;
        }
        return path;
      })();
      
      console.log('[agentClient] Connecting to agent URL:', agentRunUrl);
      console.log('[agentClient] Prompt length:', prompt.length, 'chars');
      console.log('[agentClient] Has Drasi context:', !!drasiContext?.insights);

    agentInstance = new HttpAgent({
      url: agentRunUrl,
      agentId,
    });

    // Build context array with Drasi insights if available
    const contextArray = [];
    if (drasiContext?.insights) {
      contextArray.push({
        type: "drasi-insights",
        source: "drasi-event-graph",
        timestamp: new Date().toISOString(),
        data: {
          trending: drasiContext.insights.trending?.slice(0, 5) || [],
          duplicates: drasiContext.insights.duplicates || [],
          inactiveChildren: drasiContext.insights.inactiveChildren || [],
          behaviorChanges: drasiContext.insights.behaviorChanges || [],
          stats: drasiContext.insights.stats || {},
        },
      });
    }

      subscription = agentInstance
        .run({
          threadId: crypto.randomUUID(),
          runId: crypto.randomUUID(),
          messages: [{ role: "user", content: prompt }],
          tools: [],
          context: contextArray, // Now enriched with Drasi data
          forwardedProps: {},
        })
        .subscribe({
          next: (ev: any) => {
            console.log('[agentClient] Event received:', ev.type, ev);
            handlers.onEvent(ev);
            if (
              ev.type === (mod as any).EventType?.RUN_FINISHED ||
              ev.type === EventType.RUN_FINISHED
            ) {
              handlers.onFinished();
              try {
                subscription.unsubscribe();
              } catch {}
            }
          },
          error: (err: any) => {
            console.error('[agentClient] Agent run error:', err);
            handlers.onEvent({ type: 'ERROR', delta: `❌ Error: ${err.message || err}` });
            try {
              subscription.unsubscribe();
            } catch {}
            handlers.onFinished();
          },
          complete: () => {
            console.log('[agentClient] Agent run complete');
            handlers.onFinished();
          },
        });
    } catch (error) {
      console.error('[agentClient] Failed to initialize agent:', error);
      handlers.onEvent({ type: 'ERROR', delta: `❌ Failed to start agent: ${error}` });
      handlers.onFinished();
    }
  })();
  return {
    close() {
      try {
        agentInstance?.abortRun();
      } catch {}
      try {
        subscription?.unsubscribe();
      } catch {}
    },
  };
}

export const SANTA_AGENT_ID: string =
  (import.meta as any)?.env?.VITE_SANTA_AGENT_ID || "santa";
export const ELF_AGENT_ID: string =
  (import.meta as any)?.env?.VITE_ELF_AGENT_ID || "elf";

// Collaborative multi-agent recommendation
export async function getCollaborativeRecommendation(
  childId: string,
  status: "Nice" | "Naughty" | "Unknown" = "Unknown"
): Promise<any> {
  const res = await fetch(
    `${API_URL}/api/v1/children/${encodeURIComponent(
      childId
    )}/recommendations/collaborative?status=${status}`,
    {
      method: "POST",
      headers: JSON_HEADERS,
    }
  );
  if (!res.ok)
    throw new Error(`Collaborative recommendation failed: ${res.status}`);
  return res.json();
}

// Behavior update (naughty/nice)
export interface BehaviorUpdateInput {
  newStatus: "Nice" | "Naughty" | "Unknown";
  message?: string;
}

export async function updateChildBehavior(
  childId: string,
  update: BehaviorUpdateInput
): Promise<any> {
  const res = await fetch(
    `${API_URL}/api/v1/children/${encodeURIComponent(
      childId
    )}/letters/behavior`,
    {
      method: "POST",
      headers: JSON_HEADERS,
      body: JSON.stringify(update),
    }
  );
  if (!res.ok) throw new Error(`Behavior update failed: ${res.status}`);
  return res.json();
}

// Get agent tools list
export async function getAgentTools(): Promise<any> {
  const res = await fetch(`${API_URL}/api/v1/agent-tools`, {
    headers: JSON_HEADERS,
  });
  if (!res.ok) throw new Error(`Get agent tools failed: ${res.status}`);
  return res.json();
}

// Stream recommendations via SSE
export function streamRecommendations(
  childId: string,
  status: "Nice" | "Naughty" | "Unknown",
  onUpdate: (data: any) => void,
  onComplete: () => void
): () => void {
  const eventSource = new EventSource(
    `${API_URL}/api/v1/children/${encodeURIComponent(
      childId
    )}/recommendations/stream?status=${status}`
  );

  eventSource.onmessage = (event) => {
    try {
      const data = JSON.parse(event.data);
      onUpdate(data);
      if (
        data.type === "completed" ||
        data.type === "done" ||
        data.type === "error"
      ) {
        eventSource.close();
        onComplete();
      }
    } catch (e) {
      console.error("Failed to parse SSE data:", e);
    }
  };

  eventSource.onerror = () => {
    eventSource.close();
    onComplete();
  };

  return () => eventSource.close();
}

import { useEffect, useState } from "react";
import { API_URL } from "../agentClient";

interface LiveRecommendation {
  id: string;
  suggestion: string;
  rationale: string;
}

export function useChildRecommendationsLive(childId: string) {
  const [items, setItems] = useState<LiveRecommendation[]>([]);
  useEffect(() => {
    // Always clear items when childId changes to prevent stale data
    setItems([]);
    if (!childId) {
      return;
    }
    const es = new EventSource(
      `${API_URL}/api/v1/stream/children/${encodeURIComponent(childId)}`
    );
    es.onmessage = (e) => {
      try {
        const data = JSON.parse(e.data);
        if (data.type === "recommendation-update") {
          // Possible shapes:
          // 1) { items: [...] }
          // 2) { Items: [...] }
          // 3) { recommendations: [...] }
          // 4) { id, Items: [...] } (historical set projection)
          const raw =
            data.recommendations ||
            data.items ||
            data.Items ||
            data.set?.Items ||
            [];
          const mapped: LiveRecommendation[] = (
            Array.isArray(raw) ? raw : []
          ).map((r: any) => ({
            id: r.id || r.Id || crypto.randomUUID(),
            suggestion: r.suggestion || r.Suggestion || "",
            rationale: r.rationale || r.Rationale || "",
          }));
          if (mapped.length > 0) setItems(mapped);
        }
      } catch {
        /* ignore malformed event */
      }
    };
    es.onerror = () => {
      /* keep silent for demo */
    };
    return () => es.close();
  }, [childId]);
  return items;
}

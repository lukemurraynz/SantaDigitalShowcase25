import { useEffect, useState } from "react";
import { POLLING_CONFIG } from "../constants/polling";
import type { DrasiInsights } from "../types/drasi";
import { logger } from "../utils/logger";

const API_URL = (() => {
  // If running on Azure Container Apps or Static Web Apps, ALWAYS use relative URLs
  if (
    typeof window !== "undefined" &&
    (window.location.hostname.includes("azurestaticapps") ||
      window.location.hostname.includes("azurecontainerapps"))
  ) {
    return "";
  }
  const viteApiUrl = (import.meta as any).env?.VITE_API_URL;
  if (typeof viteApiUrl === "string" && viteApiUrl.length > 0) {
    return viteApiUrl.trim().replace(/\/$/, "");
  }
  // Allow runtime injection via window.__API_BASE__ if present
  if (typeof window !== "undefined" && (window as any).__API_BASE__) {
    return (window as any).__API_BASE__.toString().replace(/\/$/, "");
  }
  // Local development fallback
  return "";
})();

/**
 * Hook to fetch real-time Drasi insights for agent context enrichment
 */
export function useDrasiContext(childId?: string) {
  const [insights, setInsights] = useState<DrasiInsights | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let consecutiveFailures = 0;

    const fetchInsights = async () => {
      try {
        const res = await fetch(`${API_URL}/api/v1/drasi/insights`);
        if (res.ok) {
          const data = await res.json();
          setInsights(data);
          consecutiveFailures = 0; // Reset on success
        } else if (res.status >= 500) {
          consecutiveFailures++;
          if (consecutiveFailures >= POLLING_CONFIG.MAX_CONSECUTIVE_FAILURES) {
            logger.warn("Drasi insights: Too many failures, stopping polling");
            clearInterval(interval);
          }
        }
      } catch {
        consecutiveFailures++;
        if (consecutiveFailures >= POLLING_CONFIG.MAX_CONSECUTIVE_FAILURES) {
          logger.warn("Drasi insights: Too many failures, stopping polling");
          clearInterval(interval);
        }
      } finally {
        setLoading(false);
      }
    };

    fetchInsights();
    const interval = setInterval(
      fetchInsights,
      POLLING_CONFIG.DRASI_INSIGHTS_INTERVAL_MS
    ); // Refresh every 30s
    return () => clearInterval(interval);
  }, []);

  // Build enhanced prompt with Drasi context
  const buildEnhancedPrompt = (basePrompt: string): string => {
    if (!insights) return basePrompt;

    const drasiContext = buildDrasiContextString(childId);

    return `You are Santa's Chief Elf analyzing real-time workshop data powered by Drasi Event Graph.

${drasiContext}

CHILD CONTEXT: ${childId || "none specified"}

USER REQUEST:
${basePrompt}

Please provide recommendations based on both the live Drasi insights and the user's request.`;
  };

  // Format Drasi context as readable string
  const buildDrasiContextString = (focusChildId?: string): string => {
    if (!insights) return "DRASI INSIGHTS: Not available";

    const lines: string[] = ["DRASI INSIGHTS (Real-time from Event Graph):"];

    // Trending items
    if (insights.trending.length > 0) {
      const topTrending = insights.trending.slice(0, 3);
      lines.push(`\n🔥 TRENDING GIFTS (Past Hour):`);
      topTrending.forEach((t, i) => {
        lines.push(`   ${i + 1}. ${t.item} - ${t.frequency} requests`);
      });
    }

    // Duplicates for focused child
    if (focusChildId) {
      const childDuplicates = insights.duplicates.filter(
        (d) => d.childId === focusChildId
      );
      if (childDuplicates.length > 0) {
        lines.push(`\n⚠️ DUPLICATE ALERTS FOR CHILD ${focusChildId}:`);
        childDuplicates.forEach((d) => {
          lines.push(`   - ${d.item} requested ${d.count} times`);
        });
      }
    }

    // Global duplicate summary
    if (insights.duplicates.length > 0) {
      lines.push(`\n⚠️ GLOBAL DUPLICATE PATTERNS:`);
      lines.push(
        `   - ${insights.duplicates.length} children with duplicate requests detected`
      );
    }

    // Inactive children
    if (insights.inactiveChildren.length > 0) {
      lines.push(`\n😴 INACTIVE CHILDREN (3+ days no activity):`);
      insights.inactiveChildren.slice(0, 3).forEach((c) => {
        lines.push(`   - ${c.childId} (last seen ${c.lastEventDays} days ago)`);
      });
    }

    // Behavior status changes (naughty/nice)
    if (focusChildId && insights.behaviorChanges) {
      const childBehavior = insights.behaviorChanges.filter(
        (b) => b.childId === focusChildId
      );
      if (childBehavior.length > 0) {
        lines.push(`\n🎅 BEHAVIOR STATUS FOR CHILD ${focusChildId}:`);
        childBehavior.forEach((b) => {
          const emoji =
            b.newStatus === "Nice"
              ? "😇"
              : b.newStatus === "Naughty"
              ? "😈"
              : "❓";
          const arrow = b.oldStatus === "Nice" ? "📉" : "📈";
          lines.push(
            `   ${emoji} Status changed: ${b.oldStatus} → ${b.newStatus} ${arrow}`
          );
          if (b.reason) {
            lines.push(`      Reason: ${b.reason}`);
          }
        });
      }
    }

    // Global behavior changes summary
    if (insights.behaviorChanges && insights.behaviorChanges.length > 0) {
      lines.push(`\n🎅 RECENT NAUGHTY/NICE STATUS CHANGES:`);
      lines.push(
        `   - ${insights.behaviorChanges.length} children with status changes detected`
      );
      const recentChanges = insights.behaviorChanges.slice(0, 3);
      recentChanges.forEach((b) => {
        const emoji =
          b.newStatus === "Nice"
            ? "😇"
            : b.newStatus === "Naughty"
            ? "😈"
            : "❓";
        lines.push(`   ${emoji} ${b.childId}: ${b.oldStatus} → ${b.newStatus}`);
      });
    }

    // Event stats
    lines.push(`\n📊 WORKSHOP METRICS:`);
    lines.push(`   - Total events processed: ${insights.stats.totalEvents}`);
    lines.push(
      `   - Active continuous queries: ${insights.stats.activeQueries}`
    );
    lines.push(`   - Data freshness: ${insights.stats.lastUpdateSeconds}s ago`);

    return lines.join("\n");
  };

  return {
    insights,
    loading,
    buildEnhancedPrompt,
    buildDrasiContextString,
    hasDrasiContext: insights !== null,
  };
}

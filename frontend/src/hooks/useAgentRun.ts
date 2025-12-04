import { useCallback, useEffect, useRef, useState } from "react";
import { cancelAgentRun, runAgent } from "../agentClient";

export interface NormalizedRunEvent {
  phase: "run" | "message" | "state" | "tool";
  kind:
    | "started"
    | "delta"
    | "end"
    | "finished"
    | "predict-start"
    | "predict-complete"
    | "tool-start"
    | "tool-delta"
    | "tool-end";
  runId: string;
  threadId?: string;
  messageId?: string;
  deltaText?: string;
  status?: string;
  field?: string;
  toolCallId?: string;
  toolName?: string;
  raw?: any;
}

interface SharedState {
  recommendationDraft?: string;
}

function mapRaw(ev: any): NormalizedRunEvent | null {
  switch (ev.type) {
    case "RUN_STARTED":
      return {
        phase: "run",
        kind: "started",
        runId: ev.runId,
        threadId: ev.threadId,
        raw: ev,
      };
    case "STATE_UPDATE_PREDICT_START":
      return {
        phase: "state",
        kind: "predict-start",
        runId: ev.runId,
        threadId: ev.threadId,
        raw: ev,
      };
    case "STATE_UPDATE_DELTA":
      return {
        phase: "state",
        kind: "delta",
        runId: ev.runId,
        threadId: ev.threadId,
        field: ev.field,
        deltaText: ev.delta,
        raw: ev,
      };
    case "STATE_UPDATE_COMPLETE":
      return {
        phase: "state",
        kind: "predict-complete",
        runId: ev.runId,
        threadId: ev.threadId,
        raw: ev,
      };
    case "TOOL_CALL_START":
      return {
        phase: "tool",
        kind: "tool-start",
        runId: ev.runId,
        threadId: ev.threadId,
        toolCallId: ev.toolCallId,
        toolName: ev.toolName,
        raw: ev,
      };
    case "TOOL_CALL_OUTPUT_DELTA":
      return {
        phase: "tool",
        kind: "tool-delta",
        runId: ev.runId,
        threadId: ev.threadId,
        toolCallId: ev.toolCallId,
        deltaText: ev.delta,
        raw: ev,
      };
    case "TOOL_CALL_END":
      return {
        phase: "tool",
        kind: "tool-end",
        runId: ev.runId,
        threadId: ev.threadId,
        toolCallId: ev.toolCallId,
        raw: ev,
      };
    case "TEXT_MESSAGE_START":
      return {
        phase: "message",
        kind: "started",
        runId: ev.runId ?? "unknown",
        messageId: ev.messageId,
        raw: ev,
      };
    case "TEXT_MESSAGE_CONTENT":
      return {
        phase: "message",
        kind: "delta",
        runId: ev.runId ?? "unknown",
        messageId: ev.messageId,
        deltaText: ev.delta,
        raw: ev,
      };
    case "TEXT_MESSAGE_END":
      return {
        phase: "message",
        kind: "end",
        runId: ev.runId ?? "unknown",
        messageId: ev.messageId,
        raw: ev,
      };
    case "RUN_FINISHED":
      return {
        phase: "run",
        kind: "finished",
        runId: ev.runId,
        threadId: ev.threadId,
        status: ev.result?.status,
        raw: ev,
      };
    default:
      return null;
  }
}

export function useAgentRun(agentId: string, drasiContext?: any) {
  const [events, setEvents] = useState<NormalizedRunEvent[]>([]);
  const [sharedState, setSharedState] = useState<SharedState>({});
  const [status, setStatus] = useState<
    "idle" | "running" | "finished" | "cancelled"
  >("idle");
  const streamRef = useRef<{ close: () => void } | null>(null);

  const start = useCallback(() => {
    if (status === "running") return;
    setStatus("running");
    setEvents([]);
    const s = runAgent(
      agentId,
      (raw) => {
        const mapped = mapRaw(raw);
        if (mapped) {
          setEvents((prev) => [...prev, mapped]);
          // Update shared state if we have prediction deltas or final snapshot
          if (mapped.phase === "state") {
            if (
              raw.type === "STATE_UPDATE_DELTA" &&
              mapped.field === "recommendationDraft"
            ) {
              setSharedState((prev) => ({
                ...prev,
                recommendationDraft:
                  (prev.recommendationDraft ?? "") + (mapped.deltaText ?? ""),
              }));
            }
            if (
              raw.type === "STATE_UPDATE_COMPLETE" &&
              raw.state?.recommendationDraft
            ) {
              setSharedState((prev) => ({
                ...prev,
                recommendationDraft: raw.state.recommendationDraft,
              }));
            }
          }
          if (mapped.kind === "finished") {
            setStatus(mapped.status === "cancelled" ? "cancelled" : "finished");
          }
        }
      },
      () => {
        /* finished callback already handled in mapping */
      },
      drasiContext
    ); // Pass Drasi context
    streamRef.current = s;
  }, [agentId, status, drasiContext]);

  const cancel = useCallback(async () => {
    if (status !== "running") return;
    try {
      await cancelAgentRun(agentId);
    } catch {
      /* swallow */
    }
    try {
      streamRef.current?.close();
    } catch {}
    setStatus("cancelled");
  }, [agentId, status]);

  useEffect(
    () => () => {
      streamRef.current?.close();
    },
    []
  );

  const transcript = events
    .filter((e) => e.phase === "message" && e.deltaText)
    .map((e) => e.deltaText)
    .join("");

  return { start, cancel, status, events, transcript, sharedState };
}

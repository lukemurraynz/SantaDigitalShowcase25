import { useCallback, useRef, useState } from "react";
import { runAgentWithPrompt } from "../agentClient";

export type ChatEvent = {
  type: string;
  data?: any;
  delta?: string;
};

export function useAgentChat(agentId: string, drasiContext?: any) {
  const [status, setStatus] = useState<"idle" | "running" | "done" | "error">(
    "idle"
  );
  const [transcript, setTranscript] = useState<string>("");
  const [events, setEvents] = useState<ChatEvent[]>([]);
  const runRef = useRef<{ close: () => void } | null>(null);

  const start = useCallback(
    (prompt: string) => {
      setStatus("running");
      setTranscript("");
      setEvents([]);
      runRef.current = runAgentWithPrompt(
        agentId,
        prompt,
        {
          onEvent: (ev: any) => {
            setEvents((prev) => [...prev, ev]);
            
            // Handle ERROR events
            if (ev.type === "ERROR") {
              setStatus("error");
              const errorMsg = ev.delta || ev.error || ev.message || "Unknown error occurred";
              setTranscript((prev) => prev + `\n\n❌ ${errorMsg}`);
              return;
            }
            
            // Handle RUN_FINISHED with error status
            if (ev.type === "RUN_FINISHED" && ev.result?.status === "failed") {
              setStatus("error");
              const errorMsg = ev.result.error || "Agent execution failed";
              if (!transcript.includes(errorMsg)) {
                setTranscript((prev) => prev + `\n\n❌ Error: ${errorMsg}`);
              }
              return;
            }
            
            const text =
              ev.delta ||
              ev.text ||
              (typeof ev.data === "string" ? ev.data : "");
            if (text) setTranscript((prev) => prev + text);
          },
          onFinished: () => {
            // Only set to "done" if not already in error state
            setStatus((current) => current === "error" ? "error" : "done");
          },
        },
        drasiContext
      ); // Pass Drasi context
    },
    [agentId, drasiContext]
  );

  const cancel = useCallback(() => {
    try {
      runRef.current?.close();
    } catch {}
    setStatus("idle");
  }, []);

  return { status, transcript, events, start, cancel };
}

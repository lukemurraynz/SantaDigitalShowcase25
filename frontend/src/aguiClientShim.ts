// Lightweight shim approximating an AG-UI HttpAgent interface
// Falls back to EventSource streaming against backend SSE endpoints.

export interface RunAgentParams { messages: { id: string; content: string }[] }
export interface RunOptions {
  onEvent?: (ev: any) => void;
  onError?: (err: any) => void;
  onFinished?: () => void;
}

export class HttpAgent {
  constructor(private baseUrl: string) {}

  async runAgent(agentId: string, params: RunAgentParams, opts: RunOptions = {}) {
    // For this shim we ignore params and just open SSE
    const es = new EventSource(`${this.baseUrl}/agents/${agentId}/run`);
    es.onmessage = (e) => {
      try {
        const parsed = JSON.parse(e.data);
        opts.onEvent?.(parsed);
        if (parsed.type === 'RunFinishedEvent') {
          opts.onFinished?.();
          es.close();
        }
      } catch (err) {
        opts.onError?.(err);
      }
    };
    es.onerror = (e) => {
      opts.onError?.(e);
    };
    return {
      close() { es.close(); },
    };
  }
}

export function createAgent(baseUrl: string) {
  return new HttpAgent(baseUrl);
}

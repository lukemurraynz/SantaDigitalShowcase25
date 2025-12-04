import { useEffect, useState } from 'react';
import { API_URL } from '../config';
import { DemoDataGenerator } from './DemoDataGenerator';

interface AgentTool {
  name: string;
  description: string;
  parameters: string[];
  category: string;
  source?: string | null;
}

interface AgentToolsResponse {
  tools: AgentTool[];
  stats: {
    drasiTools: number;
  };
  description: string;
  integration: string;
}

interface DrasiAgentDemoResponse {
  childId: string;
  timestamp: string;
  demonstration: string;
  description: string;
  drasiInsights: {
    trending: { tool: string; result: string };
    duplicates: { tool: string; result: string };
    global: { tool: string; result: string };
    inactive: { tool: string; result: string };
  };
  capabilities: string[];
  nextSteps: string[];
}

export const DrasiAgentToolsShowcase: React.FC<{ childId?: string; onChildSelected?: (childId: string) => void }> = ({ childId = 'child-123', onChildSelected }) => {
  const [tools, setTools] = useState<AgentToolsResponse | null>(null);
  const [demo, setDemo] = useState<DrasiAgentDemoResponse | null>(null);
  const [loadingTools, setLoadingTools] = useState(true);
  const [loadingDemo, setLoadingDemo] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectedTool, setSelectedTool] = useState<AgentTool | null>(null);
  const [showDataHint, setShowDataHint] = useState(true);

  useEffect(() => {
    const fetchTools = async () => {
      try {
        const res = await fetch(`${API_URL}/api/v1/agent-tools`);
        if (!res.ok) throw new Error('Failed to fetch tools');
        const data = await res.json();
        setTools(data);
        setError(null);
      } catch (e: any) {
        setError(e.message || 'Failed to load agent tools');
      } finally {
        setLoadingTools(false);
      }
    };
    fetchTools();
  }, []);

  const runDrasiDemo = async () => {
    setLoadingDemo(true);
    setError(null);
    setShowDataHint(false); // Hide hint after first demo run
    try {
      const res = await fetch(`${API_URL}/api/v1/drasi-agent-demo/${childId}`);
      if (!res.ok) throw new Error('Failed to run demo');
      const data = await res.json();
      setDemo(data);
    } catch (e: any) {
      setError(e.message || 'Failed to run Drasi demo');
    } finally {
      setLoadingDemo(false);
    }
  };

  if (loadingTools) {
    return <div style={{ textAlign: 'center', padding: '2rem', color: 'var(--text-secondary)' }}>Loading agent tools...</div>;
  }

  if (error && !tools) {
    return <div style={{ textAlign: 'center', padding: '2rem', color: 'var(--santa-red)' }}>Error: {error}</div>;
  }

  const drasiTools = tools?.tools.filter(t => t.category === 'Drasi Real-Time') || [];

  return (
    <div style={{ padding: '0 1rem' }}>
      <div style={{
        background: 'linear-gradient(135deg, var(--christmas-green), var(--santa-red))',
        padding: '2rem',
        borderRadius: 12,
        marginBottom: '1.5rem',
        boxShadow: '0 4px 8px rgba(0,0,0,0.6)'
      }}>
        <h1 style={{ margin: 0, fontSize: '2rem', color: 'white' }}>🚀 Elf Tool Arsenal - Drasi Edition</h1>
        <p style={{ margin: '0.5rem 0 0', fontSize: '1.1rem', color: 'rgba(255,255,255,0.9)' }}>
          {tools?.stats.drasiTools || drasiTools.length} Drasi-Powered Real-Time Tools
        </p>
      </div>

      {error && (
        <div style={{ padding: '1rem', background: 'var(--santa-red)', color: 'white', borderRadius: 8, marginBottom: '1rem' }}>
          ⚠️ {error}
        </div>
      )}

      <DemoDataGenerator 
        onEventSent={() => {
          // Optional: Could trigger a refresh of demo results here
          console.log('Event sent to EventHub → Drasi');
          setShowDataHint(false);
        }} 
        onChildSelected={onChildSelected}
      />

      {showDataHint && !demo && (
        <div style={{
          padding: '1.5rem',
          background: 'linear-gradient(135deg, rgba(255, 215, 0, 0.15), rgba(255, 69, 0, 0.15))',
          borderRadius: 12,
          marginBottom: '2rem',
          border: '2px dashed var(--christmas-gold)',
          textAlign: 'center'
        }}>
          <h3 style={{ margin: 0, color: 'var(--christmas-gold)', fontSize: '1.2rem' }}>
            👆 Generate Events First!
          </h3>
          <p style={{ margin: '0.5rem 0 0', color: 'var(--text-secondary)', fontSize: '.95rem' }}>
            Send some wishlist events above, then click "Run All Drasi Tools" below to see the magic! ✨
          </p>
        </div>
      )}

      <section style={{ marginBottom: '2rem' }}>
        <h2 style={{ color: 'var(--christmas-green)', marginBottom: '1rem' }}>⚡ Drasi Real-Time Tools</h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: '1rem' }}>
          {drasiTools.map(tool => (
            <div
              key={tool.name}
              onClick={() => setSelectedTool(tool)}
              style={{
                background: selectedTool?.name === tool.name ? 'var(--christmas-green)' : 'var(--bg-secondary)',
                padding: '1.5rem',
                borderRadius: 10,
                cursor: 'pointer',
                border: `2px solid ${selectedTool?.name === tool.name ? 'var(--christmas-gold)' : 'var(--border-medium)'}`,
                transition: 'all 0.3s',
                boxShadow: selectedTool?.name === tool.name ? '0 6px 12px rgba(0,0,0,0.7)' : '0 2px 4px rgba(0,0,0,0.5)'
              }}
            >
              <h3 style={{ marginTop: 0, color: selectedTool?.name === tool.name ? 'white' : 'var(--text-primary)', fontSize: '1.1rem' }}>
                {tool.name}
              </h3>
              <p style={{ fontSize: '.9rem', color: selectedTool?.name === tool.name ? 'rgba(255,255,255,0.9)' : 'var(--text-secondary)', marginBottom: '0.5rem' }}>
                {tool.description}
              </p>
              {tool.source && (
                <div style={{
                  marginTop: '0.75rem',
                  padding: '0.5rem',
                  background: selectedTool?.name === tool.name ? 'rgba(0,0,0,0.2)' : 'var(--bg-tertiary)',
                  borderRadius: 6,
                  fontSize: '.85rem'
                }}>
                  <strong style={{ color: 'var(--christmas-gold)' }}>Drasi Query:</strong> {tool.source}
                </div>
              )}
            </div>
          ))}
        </div>
      </section>

      <section style={{ background: 'var(--bg-secondary)', padding: '2rem', borderRadius: 12, boxShadow: '0 4px 8px rgba(0,0,0,0.6)', border: '1px solid var(--border-medium)' }}>
        <h2 style={{ marginTop: 0, color: 'var(--christmas-green)' }}>🎯 Live Demo: All Drasi Tools</h2>
        <div style={{
          padding: '1rem',
          background: 'var(--bg-tertiary)',
          borderRadius: 8,
          marginBottom: '1rem',
          border: '1px solid var(--christmas-gold)'
        }}>
          <p style={{ margin: 0, color: 'var(--text-primary)', fontSize: '.95rem' }}>
            <strong>💡 How to see data:</strong>
          </p>
          <ol style={{ color: 'var(--text-secondary)', fontSize: '.9rem', marginBottom: 0, marginTop: '0.5rem' }}>
            <li>First, generate some events using the <strong>"Generate Demo Wishlist Events"</strong> section above</li>
            <li>Wait 2-5 seconds for Drasi to process the events</li>
            <li>Then click the button below to fetch insights from Drasi's continuous queries</li>
            <li>Watch the <strong>"🎅 Santa's Workshop Intelligence"</strong> panel above update in real-time via SignalR!</li>
          </ol>
        </div>
        <p style={{ color: 'var(--text-secondary)', marginBottom: '1rem' }}>
          Click below to run all 4 Drasi-powered tools simultaneously and see real-time insights for child: <code style={{ background: 'var(--bg-tertiary)', padding: '2px 6px', borderRadius: 4 }}>{childId}</code>
        </p>
        <button
          onClick={runDrasiDemo}
          disabled={loadingDemo}
          style={{
            padding: '1rem 2rem',
            background: loadingDemo ? 'var(--border-medium)' : 'var(--christmas-green)',
            color: 'white',
            border: 'none',
            borderRadius: 8,
            cursor: loadingDemo ? 'not-allowed' : 'pointer',
            fontSize: '1.1rem',
            fontWeight: 700,
            boxShadow: loadingDemo ? 'none' : '0 4px 8px rgba(0,0,0,0.5)',
            transition: 'all 0.3s'
          }}
        >
          {loadingDemo ? '⏳ Running...' : '🚀 Run All Drasi Tools'}
        </button>

        {demo && (
          <div style={{ marginTop: '2rem' }}>
            <h3 style={{ color: 'var(--christmas-gold)', marginBottom: '1rem' }}>📊 Results from {demo.timestamp}</h3>

            <div style={{ display: 'grid', gap: '1rem' }}>
              <details open style={{ background: 'var(--bg-tertiary)', padding: '1rem', borderRadius: 8, border: '1px solid var(--border-medium)' }}>
                <summary style={{ cursor: 'pointer', fontWeight: 600, color: 'var(--text-primary)', fontSize: '1.05rem' }}>
                  ⚡ {demo.drasiInsights.trending.tool}
                </summary>
                <pre style={{ marginTop: '0.5rem', whiteSpace: 'pre-wrap', fontSize: '.9rem', color: 'var(--text-secondary)' }}>
                  {demo.drasiInsights.trending.result}
                </pre>
              </details>

              <details style={{ background: 'var(--bg-tertiary)', padding: '1rem', borderRadius: 8, border: '1px solid var(--border-medium)' }}>
                <summary style={{ cursor: 'pointer', fontWeight: 600, color: 'var(--text-primary)', fontSize: '1.05rem' }}>
                  🔍 {demo.drasiInsights.duplicates.tool}
                </summary>
                <pre style={{ marginTop: '0.5rem', whiteSpace: 'pre-wrap', fontSize: '.9rem', color: 'var(--text-secondary)' }}>
                  {demo.drasiInsights.duplicates.result}
                </pre>
              </details>

              <details style={{ background: 'var(--bg-tertiary)', padding: '1rem', borderRadius: 8, border: '1px solid var(--border-medium)' }}>
                <summary style={{ cursor: 'pointer', fontWeight: 600, color: 'var(--text-primary)', fontSize: '1.05rem' }}>
                  🌍 {demo.drasiInsights.global.tool}
                </summary>
                <pre style={{ marginTop: '0.5rem', whiteSpace: 'pre-wrap', fontSize: '.9rem', color: 'var(--text-secondary)' }}>
                  {demo.drasiInsights.global.result}
                </pre>
              </details>

              <details style={{ background: 'var(--bg-tertiary)', padding: '1rem', borderRadius: 8, border: '1px solid var(--border-medium)' }}>
                <summary style={{ cursor: 'pointer', fontWeight: 600, color: 'var(--text-primary)', fontSize: '1.05rem' }}>
                  😴 {demo.drasiInsights.inactive.tool}
                </summary>
                <pre style={{ marginTop: '0.5rem', whiteSpace: 'pre-wrap', fontSize: '.9rem', color: 'var(--text-secondary)' }}>
                  {demo.drasiInsights.inactive.result}
                </pre>
              </details>
            </div>

            <div style={{ marginTop: '1.5rem', padding: '1rem', background: 'var(--bg-primary)', borderRadius: 8, border: '1px solid var(--christmas-green)' }}>
              <h4 style={{ marginTop: 0, color: 'var(--christmas-green)' }}>✨ Capabilities</h4>
              <ul style={{ color: 'var(--text-secondary)', marginBottom: 0 }}>
                {demo.capabilities.map((cap, i) => (
                  <li key={i}>{cap}</li>
                ))}
              </ul>
            </div>
          </div>
        )}
      </section>
    </div>
  );
};

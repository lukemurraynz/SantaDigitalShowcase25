import React, { Suspense, lazy, useState } from 'react';
import { ELF_AGENT_ID } from '../agentClient';
import { DrasiAgentToolsShowcase } from '../components/DrasiAgentToolsShowcase';
import { ElfAvatar, ElfStatus } from '../components/ElfAvatar';
import { DrasiSignalRPanel } from '../components/DrasiSignalRPanel';
import { useAgentChat } from '../hooks/useAgentChat';
import { useDrasiContext } from '../hooks/useDrasiContext';
import { ChildDetailPage } from './ChildDetailPage';

const Assistant = lazy(() => import('../assistant/AssistantProvider'));

type Props = {
  activeChildId: string;
  onChildSelected?: (childId: string) => void;
};

type TabView = 'operations' | 'drasi-tools';

export const ElfView: React.FC<Props> = ({ activeChildId, onChildSelected }) => {
  const drasiContext = useDrasiContext(activeChildId);
  const elfChat = useAgentChat(ELF_AGENT_ID, drasiContext); // Pass Drasi context to agent
  const [task, setTask] = useState('Given the focused child, propose 3 gift options with rationale and price estimates.');
  const [activeTab, setActiveTab] = useState<TabView>('operations');

  // Enhance the onChildSelected to also switch to operations tab
  const handleChildSelected = React.useCallback((childId: string) => {
    setActiveTab('operations'); // Switch to operations tab to show recommendations
    onChildSelected?.(childId);
  }, [onChildSelected]);

  // Map agent status to elf avatar status
  const getElfStatus = (): ElfStatus => {
    switch (elfChat.status) {
      case 'running': return 'working';
      case 'done': return 'complete';
      case 'error': return 'error';
      default: return 'idle';
    }
  };

  const handleRunTask = () => {
    // Build enhanced prompt with Drasi context
    const enhancedPrompt = drasiContext.hasDrasiContext
      ? drasiContext.buildEnhancedPrompt(task)
      : `${task}\n\nFocused child: ${activeChildId || '(none)'}`;

    elfChat.start(enhancedPrompt);
  };

  // Extract tool calls from agent response for visualization
  const toolCallsDetected = React.useMemo(() => {
    const tools: string[] = [];
    if (elfChat.result && typeof elfChat.result === 'string') {
      // Parse common tool patterns from agent output
      if (elfChat.result.match(/GetChildWishlistItems|retrieved.*wishlist/i)) tools.push('GetChildWishlistItems');
      if (elfChat.result.match(/GetChildBehaviorHistory|behavior.*history/i)) tools.push('GetChildBehaviorHistory');
      if (elfChat.result.match(/QueryDrasiGraph|Drasi.*query/i)) tools.push('QueryDrasiGraph');
      if (elfChat.result.match(/trending|duplicate|inactive/i)) tools.push('Drasi Insights');
    }
    return tools;
  }, [elfChat.result]);

  return (
    <div style={{ display: 'grid', gap: '1rem' }}>
      {/* Santa's Workshop Intelligence (Live) moved to top */}
      <DrasiSignalRPanel />

      {/* Tab Navigation */}
      <div style={{ display: 'flex', gap: 8, background: 'var(--bg-secondary)', padding: '8px', borderRadius: 8, boxShadow: '0 2px 4px rgba(0,0,0,0.5)', border: '1px solid var(--border-medium)' }}>
        <button
          onClick={() => setActiveTab('operations')}
          style={{
            flex: 1,
            padding: '12px 16px',
            background: activeTab === 'operations' ? 'var(--christmas-green)' : 'var(--bg-tertiary)',
            color: activeTab === 'operations' ? 'white' : 'var(--text-secondary)',
            border: 'none',
            borderRadius: 6,
            cursor: 'pointer',
            fontWeight: 600,
            fontSize: '.95rem',
            transition: 'all 0.2s'
          }}
        >
          🎄 Elf Operations
        </button>
        <button
          onClick={() => setActiveTab('drasi-tools')}
          style={{
            flex: 1,
            padding: '12px 16px',
            background: activeTab === 'drasi-tools' ? 'var(--santa-red)' : 'var(--bg-tertiary)',
            color: activeTab === 'drasi-tools' ? 'white' : 'var(--text-secondary)',
            border: 'none',
            borderRadius: 6,
            cursor: 'pointer',
            fontWeight: 600,
            fontSize: '.95rem',
            transition: 'all 0.2s',
            position: 'relative'
          }}
        >
          🚀 Drasi Agent Tools
          <span style={{
            position: 'absolute',
            top: -6,
            right: -6,
            background: 'var(--christmas-gold)',
            color: 'var(--bg-primary)',
            fontSize: '.65rem',
            padding: '2px 6px',
            borderRadius: 10,
            fontWeight: 700
          }}>
            NEW
          </span>
        </button>
      </div>

      {/* Tab Content */}
      {activeTab === 'operations' && (
        <>
          <section style={{ background:'var(--bg-secondary)', padding:'1rem', borderRadius:8, boxShadow:'0 2px 4px rgba(0,0,0,0.5)', minHeight: 180, border: '1px solid var(--border-medium)' }}>
            <h2 style={{ marginTop:0, color: 'var(--text-primary)' }}>Elf Operations</h2>
            {activeChildId ? (
              <ChildDetailPage childId={activeChildId} />
            ) : (
              <div style={{ gridColumn:'1/-1', background:'var(--bg-tertiary)', padding:'2rem', borderRadius:8, textAlign:'center', border:'1px dashed var(--border-light)' }}>
                <p style={{ opacity:0.6, color: 'var(--text-muted)' }}>Select a child in the Santa panel to get started.</p>
              </div>
            )}
          </section>

          <section style={{ background:'var(--bg-secondary)', padding:'1rem', borderRadius:8, boxShadow:'0 2px 4px rgba(0,0,0,0.5)', border: '1px solid var(--border-medium)' }}>
            <h3 style={{ marginTop:0, display: 'flex', alignItems: 'center', gap: 12 }}>
              <ElfAvatar status={getElfStatus()} agentType="recommendation" size={40} />
              <span>
                🎄 Elf Agent Task
                {drasiContext.hasDrasiContext && (
                  <span style={{ fontSize: '.75rem', background: 'var(--christmas-green)', padding: '4px 8px', borderRadius: 4, marginLeft: 8 }}>
                    Drasi-Enhanced
                  </span>
                )}
              </span>
            </h3>
            <p style={{ opacity:.75, marginTop:0 }}>
              Runs Microsoft Agent Framework with real-time Drasi context.
              Focused child: <code>{activeChildId || 'none'}</code>
            </p>
            <textarea
              value={task}
              onChange={e=>setTask(e.target.value)}
              rows={3}
              style={{ width:'100%', background:'var(--bg-primary)', color:'var(--text-primary)', border:'1px solid var(--border-medium)', borderRadius:6, padding:8 }}
            />
            <div style={{ display:'flex', gap:8, marginTop:8 }}>
              <button
                disabled={elfChat.status==='running'}
                onClick={handleRunTask}
                style={{
                  padding:'0.5rem 0.9rem',
                  background: elfChat.status==='running' ? 'var(--border-medium)' : 'var(--christmas-green)',
                  color:'var(--text-primary)',
                  border:'none',
                  borderRadius:4,
                  cursor: elfChat.status==='running' ? 'not-allowed' : 'pointer',
                  fontWeight: 600
                }}
              >
                {drasiContext.hasDrasiContext ? '🚀 Run with Drasi Context' : 'Run Task'}
              </button>
              <button
                disabled={elfChat.status!=='running'}
                onClick={()=>elfChat.cancel()}
                style={{
                  padding:'0.5rem 0.9rem',
                  background: elfChat.status!=='running' ? 'var(--border-medium)' : 'var(--santa-red)',
                  color:'var(--text-primary)',
                  border:'none',
                  borderRadius:4,
                  cursor: elfChat.status!=='running' ? 'not-allowed' : 'pointer',
                  fontWeight: 600
                }}
              >
                Cancel
              </button>
            </div>

            {/* Show Drasi context preview when available */}
            {drasiContext.hasDrasiContext && activeChildId && (
              <details style={{ marginTop: 12, fontSize: '.85rem' }}>
                <summary style={{ cursor: 'pointer', opacity: 0.8, padding: 8, background: 'var(--bg-tertiary)', borderRadius: 4, color: 'var(--text-secondary)' }}>
                  📊 Preview Drasi Context (Click to expand)
                </summary>
                <pre style={{
                  marginTop: 8,
                  padding: 10,
                  background: 'var(--bg-primary)',
                  borderRadius: 4,
                  fontSize: '.75rem',
                  overflow: 'auto',
                  maxHeight: 200,
                  whiteSpace: 'pre-wrap',
                  color: 'var(--text-secondary)'
                }}>
                  {drasiContext.buildDrasiContextString(activeChildId)}
                </pre>
              </details>
            )}

            <div style={{ marginTop:8, fontSize:'.9rem', whiteSpace:'pre-wrap', background:'var(--bg-tertiary)', padding:10, borderRadius:6, minHeight:64, color: 'var(--text-secondary)' }}>
              {elfChat.transcript || <span style={{ opacity:.6, color: 'var(--text-muted)' }}>Task output will appear here…</span>}
            </div>

            {/* Tool Calls Indicator */}
            {toolCallsDetected.length > 0 && (
              <div style={{
                marginTop: 12,
                padding: '12px',
                background: 'linear-gradient(135deg, rgba(0,255,127,0.1), rgba(0,191,255,0.1))',
                border: '2px solid var(--christmas-green)',
                borderRadius: 8
              }}>
                <div style={{ fontSize: '.85rem', fontWeight: 700, color: 'var(--christmas-green)', marginBottom: 8 }}>
                  🛠️ Agent Tools Used:
                </div>
                <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                  {toolCallsDetected.map((tool, idx) => (
                    <span key={idx} style={{
                      background: 'var(--christmas-green)',
                      color: 'white',
                      padding: '4px 10px',
                      borderRadius: 12,
                      fontSize: '.75rem',
                      fontWeight: 600,
                      boxShadow: '0 2px 4px rgba(0,0,0,0.3)'
                    }}>
                      {tool}
                    </span>
                  ))}
                </div>
              </div>
            )}
          </section>
        </>
      )}

      {activeTab === 'drasi-tools' && (
        <DrasiAgentToolsShowcase childId={activeChildId || 'child-123'} onChildSelected={handleChildSelected} />
      )}

      <Suspense fallback={<div style={{position:'fixed',bottom:16,right:16}}>Loading assistant…</div>}>
        <Assistant />
      </Suspense>
    </div>
  );
};

export default ElfView;

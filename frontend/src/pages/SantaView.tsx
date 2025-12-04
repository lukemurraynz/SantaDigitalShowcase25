import React from 'react';
import { NotificationDto, SANTA_AGENT_ID } from '../agentClient';
import { DrasiSignalRPanel } from '../components/DrasiSignalRPanel';
import { ElfAvatar, ElfStatus } from '../components/ElfAvatar';
import { NotificationStreamPanel } from '../components/NotificationStreamPanel';
import { YearOverYearPanel } from '../components/YearOverYearPanel';
import { useAgentChat } from '../hooks/useAgentChat';
import { useDrasiContext } from '../hooks/useDrasiContext';
import { AddChild } from './AddChild';
import { ParentPortalPage } from './ParentPortalPage';

type Props = {
  childIdInput: string;
  setChildIdInput: (v: string) => void;
  onOpenChild: (id: string) => void;
  reportMeta: any;
  reportLoading: boolean;
  notifications: NotificationDto[];
  notifLoading: boolean;
  notifError: string | null;
  onRefreshNotifications: () => void;
  onRefreshReport: () => void;
};

export const SantaView: React.FC<Props> = ({
  childIdInput,
  setChildIdInput,
  onOpenChild,
  reportMeta,
  reportLoading,
  notifications,
  notifLoading,
  notifError,
  onRefreshNotifications,
  onRefreshReport,
}) => {
  const drasiContext = useDrasiContext(childIdInput);
  const santaChat = useAgentChat(SANTA_AGENT_ID, drasiContext);
  const [prompt, setPrompt] = React.useState('Summarize current wishlist trends and potential budget risks.');

  const getSantaStatus = (): ElfStatus => {
    switch (santaChat.status) {
      case 'running': return 'working';
      case 'done': return 'complete';
      case 'error': return 'error';
      default: return 'idle';
    }
  };
  return (
    <div style={{ display: 'grid', gap: '1rem' }}>
      {/* Real-time Drasi Intelligence Panel moved to ElfView to avoid duplication */}

      <section style={{ background:'var(--bg-secondary)', padding:'1rem', borderRadius:8, boxShadow:'0 2px 4px rgba(0,0,0,0.5)', border: '1px solid var(--border-medium)' }}>
        <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between' }}>
          <h2 style={{ margin:0, color: 'var(--text-primary)', display: 'flex', alignItems: 'center', gap: 8 }}>
            🎅 Santa Controls
          </h2>
          <div style={{ display:'flex', gap:'0.5rem', alignItems:'center' }}>
            <input
              style={{ padding:'0.4rem 0.6rem', borderRadius:4, border:'1px solid var(--border-medium)', background:'var(--bg-primary)', color: 'var(--text-primary)' }}
              placeholder="Focus childId"
              value={childIdInput}
              onChange={e=>setChildIdInput(e.target.value)}
            />
            <button
              style={{ padding:'0.45rem 0.9rem', background:'var(--santa-red)', color:'var(--text-primary)', border:'none', borderRadius:4, cursor:'pointer', fontWeight: 600 }}
              disabled={!childIdInput.trim()}
              onClick={()=> onOpenChild(childIdInput.trim())}
            >Open</button>
          </div>
        </div>
      </section>

      <section style={{ background:'var(--bg-secondary)', padding:'1rem', borderRadius:8, boxShadow:'0 2px 4px rgba(0,0,0,0.5)', border: '1px solid var(--border-medium)' }}>
        <h2 style={{ marginTop:0, color: 'var(--text-primary)' }}>Add Child</h2>
        <AddChild />
      </section>

      <section style={{ background:'var(--bg-secondary)', padding:'1rem', borderRadius:8, boxShadow:'0 2px 4px rgba(0,0,0,0.5)', border: '1px solid var(--border-medium)' }}>
        <h2 style={{ marginTop:0, color: 'var(--text-primary)', display: 'flex', alignItems: 'center', gap: 12 }}>
          <ElfAvatar status={getSantaStatus()} agentType="santa" size={40} />
          <span>Ask Santa Agent</span>
          {drasiContext.hasDrasiContext && (
            <span style={{ fontSize: '.75rem', background: 'var(--christmas-green)', padding: '4px 8px', borderRadius: 4, color: 'white', fontWeight: 600 }}>
              Drasi-Enhanced
            </span>
          )}
        </h2>
        <div style={{ display:'flex', gap:8, alignItems:'flex-start' }}>
          <textarea
            value={prompt}
            onChange={e=>setPrompt(e.target.value)}
            rows={3}
            style={{ flex:1, width:'100%', background:'var(--bg-primary)', color:'var(--text-primary)', border:'1px solid var(--border-medium)', borderRadius:6, padding:8 }}
            placeholder="Ask Santa about trends, budget risks, or workshop intelligence..."
          />
          <div style={{ display:'grid', gap:6 }}>
            <button disabled={santaChat.status==='running'} onClick={()=>santaChat.start(prompt)} style={{ padding:'0.5rem 0.9rem', background: santaChat.status==='running' ? 'var(--border-medium)' : 'var(--christmas-green)', color:'var(--text-primary)', border:'none', borderRadius:4, fontWeight: 600, cursor: santaChat.status==='running' ? 'not-allowed' : 'pointer' }}>Start</button>
            <button disabled={santaChat.status!=='running'} onClick={()=>santaChat.cancel()} style={{ padding:'0.5rem 0.9rem', background: santaChat.status!=='running' ? 'var(--border-medium)' : 'var(--santa-red)', color:'var(--text-primary)', border:'none', borderRadius:4, fontWeight: 600, cursor: santaChat.status!=='running' ? 'not-allowed' : 'pointer' }}>Cancel</button>
          </div>
        </div>
        {drasiContext.hasDrasiContext && (
          <div style={{ marginTop: 8, padding: 6, background: 'rgba(46, 125, 50, 0.1)', borderRadius: 4, fontSize: '.85rem', color: 'var(--text-secondary)' }}>
            ✨ Santa can now see real-time Drasi insights: {drasiContext.insights?.stats?.totalEventsProcessed || 0} events, {drasiContext.insights?.trending?.length || 0} trending items
          </div>
        )}
        <div style={{ marginTop:8, fontSize:'.9rem', whiteSpace:'pre-wrap', background:'var(--bg-tertiary)', padding:10, borderRadius:6, minHeight:64, color: 'var(--text-secondary)' }}>
          {santaChat.transcript || <span style={{ opacity:.6, color: 'var(--text-muted)' }}>Agent response will appear here…</span>}
        </div>
      </section>

      <section style={{ background:'var(--bg-secondary)', padding:'1rem', borderRadius:8, boxShadow:'0 2px 4px rgba(0,0,0,0.5)', border: '1px solid var(--border-medium)' }}>
        <h2 style={{ marginTop:0, color: 'var(--text-primary)' }}>📝 Latest Report</h2>
        {reportLoading ? (
          <div style={{ opacity:.75, color: 'var(--text-muted)' }}>Loading report...</div>
        ) : reportMeta ? (
          <div style={{ fontSize:'0.9rem', color: 'var(--text-secondary)' }}>
            <p><strong>Child:</strong> {reportMeta.childId}</p>
            <p><strong>Label:</strong> {reportMeta.label}</p>
            <p><strong>Top N:</strong> {reportMeta.topN}</p>
            <p><strong>Generated:</strong> {new Date(reportMeta.createdAt).toLocaleString()}</p>
            <a style={{ color:'var(--gold-accent)', fontWeight: 600 }} href={`/${reportMeta.path}`} target="_blank" rel="noreferrer">🎁 Open markdown report</a>
            <button 
              onClick={onRefreshReport}
              style={{ 
                marginLeft: '1rem',
                background: 'var(--christmas-green)',
                color: 'white',
                border: 'none',
                padding: '0.5rem 1rem',
                borderRadius: '4px',
                cursor: 'pointer',
                fontWeight: 600
              }}
            >
              🔄 Refresh
            </button>
          </div>
        ) : (
          <div>
            <div style={{ opacity:.75, color: 'var(--text-muted)', marginBottom: '0.75rem' }}>
              {childIdInput.trim() 
                ? `No report found for ${childIdInput}. Generate one using the "Generate Report" button in the Elf View.`
                : 'Select a child to load a report.'}
            </div>
            {childIdInput.trim() && (
              <button 
                onClick={onRefreshReport}
                style={{ 
                  background: 'var(--christmas-green)',
                  color: 'white',
                  border: 'none',
                  padding: '0.5rem 1rem',
                  borderRadius: '4px',
                  cursor: 'pointer',
                  fontWeight: 600
                }}
              >
                🔄 Check for Report
              </button>
            )}
          </div>
        )}
      </section>

      {/* Live Notification Stream */}
      {childIdInput.trim() && (
        <NotificationStreamPanel childId={childIdInput.trim()} />
      )}

      <section style={{ background:'var(--bg-secondary)', padding:'1rem', borderRadius:8, boxShadow:'0 2px 4px rgba(0,0,0,0.5)', border: '1px solid var(--border-medium)' }}>
        <ParentPortalPage />
      </section>

      {/* Year-over-Year Trends */}
      <YearOverYearPanel />
    </div>
  );
};

export default SantaView;

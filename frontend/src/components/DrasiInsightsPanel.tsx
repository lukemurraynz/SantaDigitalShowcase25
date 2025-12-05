import { useEffect, useState } from 'react';
import { API_URL } from '../config';
import { POLLING_CONFIG } from '../constants/polling';
import type { DrasiInsights } from '../types/drasi';
import { logger } from '../utils/logger';
import { Panel, PanelHeader } from './shared';

export const DrasiInsightsPanel: React.FC = () => {
  const [insights, setInsights] = useState<DrasiInsights | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [liveUpdate, setLiveUpdate] = useState<string>('');

  // Fetch insights from API
  useEffect(() => {
    let consecutiveFailures = 0;

    const fetchInsights = async () => {
      try {
        const res = await fetch(`${API_URL}/api/v1/drasi/insights`);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json();
        setInsights(data);
        setError(null);
        consecutiveFailures = 0; // Reset on success
      } catch (e: any) {
        consecutiveFailures++;
        setError(e.message || 'Failed to load Drasi insights');
        if (consecutiveFailures >= POLLING_CONFIG.MAX_CONSECUTIVE_FAILURES) {
          logger.warn('DrasiInsights: Too many failures, stopping polling');
          clearInterval(interval);
        }
      } finally {
        setLoading(false);
      }
    };

    fetchInsights();
    const interval = setInterval(fetchInsights, 15000); // Refresh every 15s
    return () => clearInterval(interval);
  }, []);

  // Stream live updates
  useEffect(() => {
    let retryCount = 0;
    const MAX_RETRIES = 3;

    const es = new EventSource(`${API_URL}/api/v1/drasi/insights/stream`);
    es.onmessage = (e) => {
      try {
        const data = JSON.parse(e.data);
        setLiveUpdate(`🎄 ${data.item} (${data.frequency} requests)`);
        setTimeout(() => setLiveUpdate(''), 5000);
        retryCount = 0; // Reset on successful message
      } catch {}
    };
    es.onerror = () => {
      retryCount++;
      if (retryCount >= MAX_RETRIES) {
        logger.warn('DrasiInsights stream: Max retries reached, stopping');
        es.close();
        return;
      }
      // Close on error - relative URLs through SWA proxy should work
      es.close();
    };
    return () => es.close();
  }, []);

  if (loading) {
    return (
      <Panel>
        <PanelHeader title="Santa's Workshop Intelligence" icon="🎅" />
        <div style={{ opacity: 0.6, color: 'var(--text-muted)' }}>Loading Drasi insights...</div>
      </Panel>
    );
  }

  if (error) {
    return (
      <Panel>
        <PanelHeader title="Santa's Workshop Intelligence" icon="🎅" />
        <div style={{ color: 'var(--status-error)', fontSize: '.9rem' }}>⚠️ {error}</div>
      </Panel>
    );
  }

  return (
    <Panel>
      <PanelHeader
        title="Santa's Workshop Intelligence"
        icon="🎅"
        badge={{ label: 'Drasi Live', variant: 'success' }}
      />

      {/* Live Update Banner */}
      {liveUpdate && (
        <div style={{
          background: 'linear-gradient(90deg, var(--christmas-green), var(--christmas-green-dark))',
          padding: '8px 12px',
          borderRadius: 6,
          marginBottom: 12,
          animation: 'pulse 2s ease-in-out infinite',
          fontSize: '.9rem',
          color: 'var(--text-primary)',
          fontWeight: 600
        }}>
          ⚡ {liveUpdate}
        </div>
      )}

      {/* Stats Row */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 8, marginBottom: 16 }}>
        <div style={{ background: 'var(--bg-tertiary)', padding: 8, borderRadius: 6, textAlign: 'center', border: '1px solid var(--border-dark)' }}>
          <div style={{ fontSize: '1.5rem', fontWeight: 'bold', color: 'var(--christmas-green)' }}>
            {insights?.stats.totalEvents || 0}
          </div>
          <div style={{ fontSize: '.75rem', opacity: 0.7, color: 'var(--text-muted)' }}>Total Events</div>
        </div>
        <div style={{ background: 'var(--bg-tertiary)', padding: 8, borderRadius: 6, textAlign: 'center', border: '1px solid var(--border-dark)' }}>
          <div style={{ fontSize: '1.5rem', fontWeight: 'bold', color: 'var(--gold-accent)' }}>
            {insights?.stats.activeQueries || 0}
          </div>
          <div style={{ fontSize: '.75rem', opacity: 0.7, color: 'var(--text-muted)' }}>Active Queries</div>
        </div>
        <div style={{ background: 'var(--bg-tertiary)', padding: 8, borderRadius: 6, textAlign: 'center', border: '1px solid var(--border-dark)' }}>
          <div style={{ fontSize: '1.5rem', fontWeight: 'bold', color: 'var(--santa-red)' }}>
            {insights?.stats.lastUpdateSeconds || 0}s
          </div>
          <div style={{ fontSize: '.75rem', opacity: 0.7, color: 'var(--text-muted)' }}>Last Update</div>
        </div>
      </div>

      {/* Trending Items */}
      <div style={{ marginBottom: 12 }}>
        <h4 style={{ fontSize: '.9rem', marginBottom: 8, opacity: 0.9, color: 'var(--text-primary)' }}>
          🔥 Trending Gifts (Past Hour)
        </h4>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          {insights?.trending.slice(0, 3).map((item, i) => (
            <div key={i} style={{ background: 'var(--bg-tertiary)', padding: '6px 10px', borderRadius: 4, display: 'flex', justifyContent: 'space-between', alignItems: 'center', border: '1px solid var(--border-dark)' }}>
              <span style={{ fontSize: '.85rem', color: 'var(--text-secondary)' }}>{item.item}</span>
              <span style={{ fontSize: '.75rem', background: 'var(--christmas-green)', padding: '2px 8px', borderRadius: 3, fontWeight: 600, color: 'var(--text-primary)' }}>
                {item.frequency} requests
              </span>
            </div>
          ))}
        </div>
      </div>

      {/* Duplicate Alerts */}
      <div style={{ marginBottom: 12 }}>
        <h4 style={{ fontSize: '.9rem', marginBottom: 8, opacity: 0.9, color: 'var(--text-primary)' }}>
          ⚠️ Duplicate Requests Detected
        </h4>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          {insights?.duplicates.slice(0, 2).map((dup, i) => (
            <div key={i} style={{ background: 'var(--bg-tertiary)', padding: '6px 10px', borderRadius: 4, fontSize: '.85rem', border: '1px solid var(--border-dark)', color: 'var(--text-secondary)' }}>
              Child <code style={{ background: 'var(--bg-primary)', padding: '2px 6px', borderRadius: 3, color: 'var(--gold-accent)' }}>{dup.childId}</code>:
              <span style={{ color: 'var(--status-warning)', marginLeft: 6 }}>{dup.item}</span>
              <span style={{ opacity: 0.7, marginLeft: 6 }}>({dup.count}x)</span>
            </div>
          ))}
        </div>
      </div>

      {/* Inactive Children */}
      {insights?.inactiveChildren && insights.inactiveChildren.length > 0 && (
        <div>
          <h4 style={{ fontSize: '.9rem', marginBottom: 8, opacity: 0.9, color: 'var(--text-primary)' }}>
            😴 Inactive Children (3+ Days)
          </h4>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {insights.inactiveChildren.slice(0, 2).map((child, i) => (
              <div key={i} style={{ background: 'var(--bg-tertiary)', padding: '6px 10px', borderRadius: 4, fontSize: '.85rem', border: '1px solid var(--border-dark)', color: 'var(--text-secondary)' }}>
                Child <code style={{ background: 'var(--bg-primary)', padding: '2px 6px', borderRadius: 3, color: 'var(--gold-accent)' }}>{child.childId}</code>
                <span style={{ opacity: 0.7, marginLeft: 6 }}>({child.lastEventDays} days ago)</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Powered By Footer */}
      <div style={{
        marginTop: 16,
        paddingTop: 12,
        borderTop: '1px solid var(--border-dark)',
        fontSize: '.75rem',
        opacity: 0.6,
        textAlign: 'center',
        color: 'var(--text-muted)'
      }}>
        Powered by Drasi Event Graph • Real-time Continuous Queries
      </div>
    </Panel>
  );
};

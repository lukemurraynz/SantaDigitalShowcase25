import { useEffect, useState } from 'react';
import { API_URL } from '../agentClient';
import { Panel, StatusBadge } from './shared';

interface Notification {
  id: string;
  type: string;
  message: string;
  relatedId?: string;
  state: string;
  timestamp?: string;
}

interface NotificationStreamPanelProps {
  childId: string;
}

export const NotificationStreamPanel: React.FC<NotificationStreamPanelProps> = ({ childId }) => {
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // Robust SSE stream with auto-retry and backoff
    let es: EventSource | null = null;
    let retryMs = 1000;
    let retryCount = 0;
    const MAX_RETRIES = 10;
    let isClosed = false;

    const connect = () => {
      if (isClosed || retryCount >= MAX_RETRIES) {
        if (retryCount >= MAX_RETRIES) {
          console.warn('NotificationStream: Max retries reached, stopping');
          setError('Unable to connect after multiple attempts. Refresh to retry.');
        }
        return;
      }
      try {
        const streamUrl = `${API_URL}/api/v1/notifications/stream/${childId}`;
        console.log('[Notifications] Opening stream', streamUrl);
        es = new EventSource(streamUrl);

        es.onopen = () => {
          setConnected(true);
          setError(null);
          // Reset backoff and retry count on successful connect
          retryMs = 1000;
          retryCount = 0;
        };

        const handlePayload = (raw: string) => {
          if (!raw) return;
          try {
            const payload = JSON.parse(raw);
            const items: Notification[] = Array.isArray(payload) ? payload : [payload];
            setNotifications(prev => {
              const next = [...items, ...prev];
              return next.slice(0, 20);
            });
          } catch (e) {
            if (process.env.NODE_ENV !== 'production') {
              console.warn('Skipping non-JSON SSE message');
            }
          }
        };

        // Default message handler (when server omits explicit event type)
        es.onmessage = (event) => handlePayload(event.data);

        // Named event handler from backend ("notification")
        es.addEventListener('notification', (e: MessageEvent) => handlePayload(e.data));
        // Optional resume event for Last-Event-ID support
        es.addEventListener('resume', () => setConnected(true));

        es.onerror = (err) => {
          console.error('[Notifications] Stream error:', err);
          setConnected(false);
          setError('Connection lost. Retrying...');
          // Close current stream and schedule reconnect with exponential backoff (max 30s)
          try { es && es.close(); } catch {}
          es = null;
          retryCount++;
          retryMs = Math.min(retryMs * 2, 30000);
          if (!isClosed && retryCount < MAX_RETRIES) {
            setTimeout(connect, retryMs);
          }
        };
      } catch (e) {
        console.error('[Notifications] Failed to initialize SSE:', e);
        setConnected(false);
        setError('Unable to connect. Retrying...');
        retryCount++;
        retryMs = Math.min(retryMs * 2, 30000);
        if (retryCount < MAX_RETRIES) {
          setTimeout(connect, retryMs);
        }
      }
    };

    connect();

    return () => {
      isClosed = true;
      try { es && es.close(); } catch {}
    };
  }, [childId]);

  // Fetch initial notifications
  useEffect(() => {
    const fetchInitial = async () => {
      try {
        // Pull recent notifications, scoped to child when available
        const url = childId
          ? `${API_URL}/api/v1/notifications?limit=10&childId=${encodeURIComponent(childId)}`
          : `${API_URL}/api/v1/notifications?limit=10`;
        console.log('[Notifications] Fetching list from', url);
        const res = await fetch(url);
        console.log('[Notifications] Response status', res.status);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json();
        const items: Notification[] = Array.isArray(data?.items) ? data.items : Array.isArray(data) ? data : [];
        setNotifications(items.slice(0, 20));
      } catch (e: any) {
        console.error('[Notifications] Fetch failed for list:', e);
      }
    };

    fetchInitial();
  }, [childId]);

  const getTypeIcon = (type: string) => {
    switch (type) {
      case 'logistics': return '📦';
      case 'recommendation': return '🎁';
      case 'wishlist': return '🎄';
      case 'behavior': return '⭐';
      default: return '🔔';
    }
  };

  const getStateColor = (state: string) => {
    switch (state) {
      case 'unread': return 'var(--christmas-green)';
      case 'read': return 'var(--text-muted)';
      case 'new': return 'var(--christmas-red)';
      default: return 'var(--text-muted)';
    }
  };

  return (
    <Panel maxHeight="400px">
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: 8,
        marginBottom: 12,
        justifyContent: 'space-between'
      }}>
        <h3 style={{ margin: 0, color: 'var(--text-primary)', display: 'flex', alignItems: 'center', gap: 8 }}>
          🔔 Live Notifications
        </h3>
        <StatusBadge
          label={connected ? '● Live' : '○ Connecting...'}
          variant={connected ? 'success' : 'neutral'}
        />
      </div>

      {error && (
        <div style={{
          color: 'var(--status-error)',
          fontSize: '.85rem',
          marginBottom: 8,
          padding: '6px 8px',
          background: 'rgba(255, 0, 0, 0.1)',
          borderRadius: 4
        }}>
          ⚠️ {error}
        </div>
      )}

      <div style={{
        flex: 1,
        overflowY: 'auto',
        display: 'flex',
        flexDirection: 'column',
        gap: 8
      }}>
        {notifications.length === 0 ? (
          <div style={{
            textAlign: 'center',
            padding: '2rem',
            color: 'var(--text-muted)',
            fontSize: '.9rem'
          }}>
            No notifications yet...
          </div>
        ) : (
          notifications.map((notif, idx) => (
            <div
              key={notif.id || idx}
              style={{
                background: 'var(--bg-tertiary)',
                padding: '10px 12px',
                borderRadius: 6,
                border: `1px solid ${notif.state === 'new' || notif.state === 'unread' ? 'var(--christmas-green)' : 'var(--border-dark)'}`,
                transition: 'all 0.2s ease',
                animation: idx === 0 && notif.state === 'new' ? 'slideIn 0.3s ease-out' : 'none'
              }}
            >
              <div style={{ display: 'flex', alignItems: 'start', gap: 10 }}>
                <span style={{ fontSize: '1.5rem' }}>{getTypeIcon(notif.type)}</span>
                <div style={{ flex: 1 }}>
                  <div style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    marginBottom: 4
                  }}>
                    <span style={{
                      fontSize: '.75rem',
                      fontWeight: 600,
                      textTransform: 'uppercase',
                      color: getStateColor(notif.state)
                    }}>
                      {notif.type}
                    </span>
                    {notif.timestamp && (
                      <span style={{ fontSize: '.7rem', color: 'var(--text-muted)' }}>
                        {new Date(notif.timestamp).toLocaleTimeString()}
                      </span>
                    )}
                  </div>
                  <div style={{
                    fontSize: '.9rem',
                    color: 'var(--text-primary)',
                    lineHeight: 1.4
                  }}>
                    {notif.message}
                  </div>
                  {notif.relatedId && (
                    <div style={{
                      fontSize: '.75rem',
                      color: 'var(--text-muted)',
                      marginTop: 4,
                      fontFamily: 'monospace'
                    }}>
                      ID: {notif.relatedId.substring(0, 8)}...
                    </div>
                  )}
                </div>
              </div>
            </div>
          ))
        )}
      </div>

      <style>{`
        @keyframes slideIn {
          from {
            transform: translateX(-20px);
            opacity: 0;
          }
          to {
            transform: translateX(0);
            opacity: 1;
          }
        }
      `}</style>
    </Panel>
  );
};

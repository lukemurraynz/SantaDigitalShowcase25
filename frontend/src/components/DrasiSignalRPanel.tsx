import * as signalR from '@microsoft/signalr';
import React from 'react';
import { Panel, PanelHeader, StatusBadgeVariant, StreamItem, StreamList } from './shared';

// Helper to check if a URL is an insecure HTTP endpoint
const isInsecureUrl = (url: string): boolean => {
  return !!url && url.toLowerCase().startsWith('http://');
};

// Helper to check if we're on a secure (HTTPS) page
const isSecurePage = (): boolean => {
  if (typeof window === 'undefined') return false;
  return window.location.protocol === 'https:';
};

// Helper to normalize protocol to match the page to avoid mixed-content
const normalizeProtocol = (inputUrl: string): string => {
  try {
    const pageProtocol = typeof window !== 'undefined' ? window.location.protocol : 'http:';
    const u = new URL(inputUrl);
    // If page is https, force https for hub to avoid mixed content
    if (pageProtocol === 'https:' && u.protocol !== 'https:') {
      u.protocol = 'https:';
    }
    return u.toString();
  } catch {
    // If relative or invalid URL, return as-is
    return inputUrl;
  }
};

// Resolve SignalR hub URL in order of reliability: env -> window -> same-origin fallback
// IMPORTANT: When served from Container App, use relative path for same-origin connection
const getSignalRUrl = (): string => {
  const viteEnv: any = (import.meta as any).env || {};
  const onSecurePage = isSecurePage();

  // 1) If on Azure Container Apps or Static Web Apps, use relative path (same origin)
  if (typeof window !== 'undefined' &&
      (window.location.hostname.includes('azurecontainerapps') ||
       window.location.hostname.includes('azurestaticapps'))) {
    return '/api/hub'; // Relative URL - same origin as frontend
  }

  // 2) Env-provided base (for local development overrides)
  if (viteEnv.VITE_SIGNALR_URL?.trim()) {
    const base = viteEnv.VITE_SIGNALR_URL.trim().replace(/\/$/, '');
    const hubUrl = `${base}/hub`;

    // If on HTTPS and the URL is HTTP, fall through to safer options
    if (onSecurePage && isInsecureUrl(hubUrl)) {
      console.warn('[DrasiSignalR] Skipping insecure VITE_SIGNALR_URL on HTTPS page:', hubUrl);
    } else {
      return hubUrl;
    }
  }

  // 3) Window-injected absolute hub (from index.html prebuild patch)
  if (typeof window !== 'undefined' && (window as any).__SIGNALR_HUB__) {
    const hub = (window as any).__SIGNALR_HUB__.toString();

    // If on HTTPS and the hub URL is HTTP, fall through to safer options
    if (onSecurePage && isInsecureUrl(hub)) {
      console.warn('[DrasiSignalR] Skipping insecure window.__SIGNALR_HUB__ on HTTPS page:', hub);
    } else {
      return hub;
    }
  }

  // 4) Fallback to same-origin path
  return '/api/hub';
};

const SIGNALR_HUB_URL = getSignalRUrl();
console.log('[DrasiSignalR] Hub URL:', SIGNALR_HUB_URL);

// Shared SignalR connection singleton
let sharedConnection: signalR.HubConnection | null = null;
let connectionPromise: Promise<void> | null = null;
const queryHandlers = new Map<string, Set<(event: any) => void>>();

// Get or create shared connection
const getSharedConnection = async (): Promise<signalR.HubConnection> => {
  if (sharedConnection?.state === signalR.HubConnectionState.Connected) {
    return sharedConnection;
  }

  if (connectionPromise) {
    await connectionPromise;
    return sharedConnection!;
  }

  if (!sharedConnection) {
    sharedConnection = new signalR.HubConnectionBuilder()
      .withUrl(SIGNALR_HUB_URL, {
        skipNegotiation: false,
        transport: signalR.HttpTransportType.ServerSentEvents | signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Register hub info handler once
    sharedConnection.on('hub.info', (info: any) => {
      console.log('[SignalR] Hub info:', info);
    });

    // Handle reconnection
    sharedConnection.onreconnected(() => {
      console.log('[SignalR] Reconnected, reloading queries...');
      // Trigger reload for all active queries
      queryHandlers.forEach((_, queryId) => {
        // Queries will reload via their individual useEffect hooks
      });
    });
  }

  connectionPromise = sharedConnection.start()
    .then(() => {
      console.log('[SignalR] Shared connection established');
      connectionPromise = null;
    })
    .catch((err) => {
      console.error('[SignalR] Connection error:', err);
      connectionPromise = null;
      throw err;
    });

  await connectionPromise;
  return sharedConnection;
};

// Register a handler for a query
const registerQueryHandler = (queryId: string, handler: (event: any) => void) => {
  if (!queryHandlers.has(queryId)) {
    queryHandlers.set(queryId, new Set());
    // Register the query handler on the connection
    sharedConnection?.on(queryId, (event: any) => {
      queryHandlers.get(queryId)?.forEach(h => h(event));
    });
  }
  queryHandlers.get(queryId)!.add(handler);
};

// Unregister a handler for a query
const unregisterQueryHandler = (queryId: string, handler: (event: any) => void) => {
  const handlers = queryHandlers.get(queryId);
  if (handlers) {
    handlers.delete(handler);
    if (handlers.size === 0) {
      queryHandlers.delete(queryId);
      sharedConnection?.off(queryId);
    }
  }
};

// Custom hook for SignalR with shared connection
function useSignalRQuery<T>(queryId: string, sortFn?: (item: T) => number) {
  const [data, setData] = React.useState<Map<string, T>>(new Map());
  const [connection, setConnection] = React.useState<signalR.HubConnection | null>(null);
  const mounted = React.useRef(false);

  React.useEffect(() => {
    mounted.current = true;

    const handleUpdate = (event: any) => {
      if (!mounted.current) return;

      try {
        const getKey = (obj: any) => JSON.stringify(obj);

        // Use functional state update to avoid stale closure issue
        setData(prevData => {
          const currentData = new Map(prevData);

          switch (event.op) {
            case 'i': // insert
              if (event.payload?.after) {
                currentData.set(getKey(event.payload.after), event.payload.after);
              }
              break;
            case 'u': // update
              if (event.payload?.before) {
                currentData.delete(getKey(event.payload.before));
              }
              if (event.payload?.after) {
                currentData.set(getKey(event.payload.after), event.payload.after);
              }
              break;
            case 'd': // delete
              if (event.payload?.before) {
                currentData.delete(getKey(event.payload.before));
              }
              break;
            case 'x': // control signal
              if (event.payload?.kind === 'deleted') {
                currentData.clear();
              }
              break;
          }

          return currentData;
        });
      } catch (err) {
        console.error(`[SignalR ${queryId}] Update error:`, err);
      }
    };

    const startConnection = async () => {
      try {
        const conn = await getSharedConnection();
        
        // Register this query's handler FIRST so we don't miss events
        registerQueryHandler(queryId, handleUpdate);
        
        // Set connection immediately so UI shows as connected
        setConnection(conn);

        // Request initial data via reload stream (async, non-blocking)
        const reloadStream = conn.stream('reload', queryId);
        const reloadData = new Map<string, T>();
        const getKey = (obj: any) => JSON.stringify(obj);

        reloadStream.subscribe({
          next: (event: any) => {
            if (event.op === 'r' && event.payload?.after) {
              reloadData.set(getKey(event.payload.after), event.payload.after);
            }
          },
          complete: () => {
            if (mounted.current) {
              // Only update if we got data (don't clear existing fallback data)
              if (reloadData.size > 0) {
                setData(reloadData);
              }
            }
          },
          error: (err) => {
            console.error(`[SignalR ${queryId}] Reload error:`, err);
          }
        });
      } catch (err) {
        console.error(`[SignalR ${queryId}] Connection error:`, err);
      }
    };

    startConnection();

    return () => {
      mounted.current = false;
      unregisterQueryHandler(queryId, handleUpdate);
    };
  }, [queryId]);

  // Convert map to sorted array
  const items = React.useMemo(() => {
    const arr = Array.from(data.values());
    if (sortFn) {
      arr.sort((a, b) => sortFn(a) - sortFn(b));
    }
    return arr;
  }, [data, sortFn]);

  return { items, connected: connection?.state === signalR.HubConnectionState.Connected };
}

interface TrendingItem {
  item: string;
  frequency: number;
}

interface DuplicateItem {
  childId: string;
  item: string;
  duplicateCount: number;
}

interface InactiveChild {
  childId: string;
  lastEvent: string;
  daysSinceLastEvent: number;
}

interface BehaviorChange {
  childId: string;
  newStatus: string;
  previousStatus: string;
  changedAt: string;
}

const getConnectionBadge = (status: 'connecting' | 'connected' | 'error'): { label: string; variant: StatusBadgeVariant } => {
  switch (status) {
    case 'connected':
      return { label: '🟢 SignalR', variant: 'success' };
    case 'error':
      return { label: '🔴 Offline', variant: 'error' };
    default:
      return { label: '🟡 Connecting', variant: 'warning' };
  }
};

export const DrasiSignalRPanel: React.FC = () => {
  const trending = useSignalRQuery<TrendingItem>('wishlist-trending-1h', (item) => -item.frequency);
  const duplicates = useSignalRQuery<DuplicateItem>('wishlist-duplicates-by-child', (item) => -item.duplicateCount);
  const behaviors = useSignalRQuery<BehaviorChange>('behavior-status-changes', (item) => -new Date(item.changedAt).getTime());
  const inactive = useSignalRQuery<InactiveChild>('wishlist-inactive-children-3d', (item) => -item.daysSinceLastEvent);

  // Fallback: Load data from REST API if SignalR hasn't received data yet
  const [restData, setRestData] = React.useState<any>(null);
  
  React.useEffect(() => {
    const loadInitialData = async () => {
      try {
        console.log('[DrasiSignalR] Fetching REST API data...');
        const response = await fetch('/api/v1/drasi/insights');
        if (response.ok) {
          const data = await response.json();
          console.log('[DrasiSignalR] REST API response:', {
            trending: data.trending?.length || 0,
            duplicates: data.duplicates?.length || 0,
            inactiveChildren: data.inactiveChildren?.length || 0,
            duplicatesSample: data.duplicates?.slice(0, 2)
          });
          
          // Map backend field names to frontend interface names
          const mappedData = {
            trending: data.trending || [],
            duplicates: (data.duplicates || []).map((d: any) => ({
              childId: d.childId,
              item: d.item,
              duplicateCount: d.count || d.duplicateCount || 2 // Backend uses 'count', frontend uses 'duplicateCount'
            })),
            inactiveChildren: (data.inactiveChildren || []).map((c: any) => ({
              childId: c.childId,
              daysSinceLastEvent: c.lastEventDays || c.daysSinceLastEvent || 3
            }))
          };
          
          console.log('[DrasiSignalR] Mapped REST data:', {
            duplicates: mappedData.duplicates.length,
            duplicatesSample: mappedData.duplicates.slice(0, 2)
          });
          
          setRestData(mappedData);
        }
      } catch (err) {
        console.error('[DrasiSignalR] Failed to load initial data:', err);
      }
    };
    
    // Load initial data immediately for fast display, then refresh every 30 seconds
    loadInitialData();
    const interval = setInterval(loadInitialData, 30000);
    return () => clearInterval(interval);
  }, []);

  const connected = trending.connected || duplicates.connected || behaviors.connected || inactive.connected;
  const connectionStatus: 'connecting' | 'connected' | 'error' =
    connected ? 'connected' : 'connecting';

  const badge = getConnectionBadge(connectionStatus);
  
  // Use SignalR data if available, otherwise fall back to REST API data
  // IMPORTANT: Prefer SignalR when it has data, but show REST data as fallback
  // Don't switch from REST to SignalR empty arrays - wait for actual SignalR data
  const trendingItems = trending.items.length > 0 ? trending.items : (restData?.trending || []);
  const duplicateItems = duplicates.items.length > 0 ? duplicates.items : (restData?.duplicates || []);
  const inactiveItems = inactive.items.length > 0 ? inactive.items : (restData?.inactiveChildren || []);
  
  // Debug logging
  console.log('[DrasiSignalR] Data sources:', {
    trending: { signalr: trending.items.length, rest: restData?.trending?.length || 0, using: trendingItems.length },
    duplicates: { signalr: duplicates.items.length, rest: restData?.duplicates?.length || 0, using: duplicateItems.length },
    inactive: { signalr: inactive.items.length, rest: restData?.inactiveChildren?.length || 0, using: inactiveItems.length },
    connected: trending.connected || duplicates.connected
  });
  
  // Behavior changes only come from SignalR (not in REST API)
  const behaviorItems = behaviors.items;

  // Filter out behavior-related messages that shouldn't appear in wishlist sections
  const behaviorKeywords = ['behavior', 'behave', 'chores', 'naughty', 'nice list', 'improve', 'BEHAVIOR REPORT'];
  const isBehaviorMessage = (text: string) => {
    if (!text) return false;
    const lowerText = text.toLowerCase();
    return behaviorKeywords.some(keyword => lowerText.includes(keyword.toLowerCase()));
  };

  // Filter trending items to exclude behavior messages
  const filteredTrendingItems = trendingItems.filter((item: any) => 
    item.item && !isBehaviorMessage(item.item)
  );

  // Filter duplicate items to exclude behavior messages
  const filteredDuplicateItems = duplicateItems.filter((item: any) => 
    item.item && !isBehaviorMessage(item.item)
  );
  
  console.log('[DrasiSignalR] Filtered duplicates:', {
    original: duplicateItems.length,
    filtered: filteredDuplicateItems.length,
    sample: filteredDuplicateItems.slice(0, 3)
  });

  return (
    <Panel style={{ marginBottom: '1rem' }}>
      <PanelHeader
        title="Santa's Workshop Intelligence (Live)"
        icon="🎅"
        badge={badge}
      />

      {/* Trending Items */}
      <StreamList
        title="Trending Items (Last Hour)"
        titleIcon="🔥"
        titleColor="var(--christmas-gold)"
      >
        {filteredTrendingItems.slice(0, 5).map((item: any, idx: number) => (
          <StreamItem key={item.item || idx}>
            <span>🎄 {item.item}</span>
            <span style={{ color: 'var(--christmas-gold)', fontWeight: 600 }}>
              {item.frequency} requests
            </span>
          </StreamItem>
        ))}
      </StreamList>

      {/* Duplicate Wishlists */}
      <StreamList
        title="Duplicate Items Detected"
        titleIcon="⚠️"
        titleColor="var(--status-warning)"
        isEmpty={filteredDuplicateItems.length === 0}
        emptyMessage="No duplicate wishlist items detected"
      >
        {filteredDuplicateItems.slice(0, 5).map((item: any, idx: number) => (
          <StreamItem key={`${item.childId}-${item.item}-${idx}`}>
            <span>👦 {item.childId}: {item.item}</span>
            <span style={{ color: 'var(--status-warning)', fontWeight: 600 }}>
              ×{item.count || item.duplicateCount || 2}
            </span>
          </StreamItem>
        ))}
      </StreamList>

      {/* Naughty/Nice Status Changes - Real-time from Drasi */}
      <StreamList
        title="Naughty/Nice Status Changes"
        titleIcon="🎅"
        titleColor="var(--christmas-gold)"
      >
        {behaviorItems.map((item) => (
          <StreamItem key={`${item.childId}-${item.changedAt}`}>
            <span>
              {item.newStatus === 'Nice' ? '😇' : '😈'} {item.childId}
            </span>
            <span style={{
              color: item.newStatus === 'Nice' ? 'var(--christmas-green)' : 'var(--santa-red)',
              fontWeight: 600,
              fontSize: '0.85rem'
            }}>
              → {item.newStatus}
            </span>
          </StreamItem>
        ))}
      </StreamList>

      {/* Inactive Children */}
      <StreamList
        title="Inactive Children (3+ Days)"
        titleIcon="😴"
        titleColor="var(--status-error)"
      >
        {inactiveItems.slice(0, 5).map((item: any, idx: number) => (
          <StreamItem key={item.childId || idx}>
            <span>👶 {item.childId}</span>
            <span style={{ color: 'var(--text-muted)', fontSize: '0.85rem' }}>
              {item.lastEventDays || item.daysSinceLastEvent || 3} days ago
            </span>
          </StreamItem>
        ))}
      </StreamList>
    </Panel>
  );
};

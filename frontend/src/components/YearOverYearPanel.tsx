import { useEffect, useState } from 'react';
import { API_URL } from '../config';
import type { DrasiInsights, YearOverYearTrends } from '../types/drasi';
import { Panel, PanelHeader, StatusBadge } from './shared';

export const YearOverYearPanel: React.FC = () => {
  const [trends, setTrends] = useState<YearOverYearTrends | null>(null);
  const [drasiInsights, setDrasiInsights] = useState<DrasiInsights | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const fetchTrends = async () => {
      try {
        const res = await fetch(`${API_URL}/api/v1/trends/year-over-year`);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json();
        setTrends(data);
        setError(null);
      } catch (e: any) {
        setError(e.message || 'Failed to load trends');
      } finally {
        setLoading(false);
      }
    };

    const fetchDrasiInsights = async () => {
      try {
        const res = await fetch(`${API_URL}/api/v1/drasi/insights`);
        if (res.ok) {
          const data = await res.json();
          setDrasiInsights(data);
        }
      } catch {
        // Silent fail - Drasi insights are optional enhancement
      }
    };

    fetchTrends();
    fetchDrasiInsights();

    const trendsInterval = setInterval(fetchTrends, 60000); // Refresh every 60s
    const drasiInterval = setInterval(fetchDrasiInsights, 10000); // Refresh every 10s for real-time

    return () => {
      clearInterval(trendsInterval);
      clearInterval(drasiInterval);
    };
  }, []);

  if (loading) {
    return (
      <Panel>
        <PanelHeader title="Year-over-Year Trends" icon="📊" />
        <div style={{ opacity: 0.6, color: 'var(--text-muted)' }}>Loading historical data...</div>
      </Panel>
    );
  }

  if (error || !trends) {
    return (
      <Panel>
        <PanelHeader title="Year-over-Year Trends" icon="📊" />
        <div style={{ color: 'var(--status-error)', fontSize: '.9rem' }}>⚠️ {error || 'No data available'}</div>
      </Panel>
    );
  }

  const { insights, metadata } = trends;
  const trendIcon = insights.volumeChange.trend === 'up' ? '📈' : insights.volumeChange.trend === 'down' ? '📉' : '➡️';
  const trendColor = insights.volumeChange.trend === 'up' ? 'var(--christmas-green)' : insights.volumeChange.trend === 'down' ? 'var(--status-error)' : 'var(--gold-accent)';

  return (
    <Panel>
      <PanelHeader
        title={`Christmas Trends: ${metadata.currentYear} vs ${metadata.comparisonYear}`}
        icon="📊"
        badge={{ label: 'Historical', variant: 'info' }}
      />

      {/* Volume Change Card */}
      <div style={{
        background: 'linear-gradient(135deg, var(--bg-tertiary), var(--bg-primary))',
        padding: '1rem',
        borderRadius: 8,
        marginBottom: 16,
        border: '2px solid var(--border-medium)',
        textAlign: 'center'
      }}>
        <div style={{ fontSize: '3rem', marginBottom: 8 }}>{trendIcon}</div>
        <div style={{ fontSize: '2rem', fontWeight: 'bold', color: trendColor, marginBottom: 4 }}>
          {insights.volumeChange.percentChange > 0 ? '+' : ''}{insights.volumeChange.percentChange}%
        </div>
        <div style={{ fontSize: '.9rem', color: 'var(--text-secondary)', marginBottom: 8 }}>
          Request Volume Change
        </div>
        <div style={{ fontSize: '.8rem', opacity: 0.7, color: 'var(--text-muted)' }}>
          {metadata.currentPeriod} ({insights.volumeChange.current} requests) vs<br />
          {metadata.historicalPeriod} ({insights.volumeChange.historical} requests)
        </div>
      </div>

      {/* Side-by-Side Comparison */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 16 }}>
        {/* Current Year - Use Drasi real-time data if available */}
        <div>
          <h4 style={{ fontSize: '.9rem', marginBottom: 8, color: 'var(--christmas-green)', borderBottom: '2px solid var(--christmas-green)', paddingBottom: 4, display: 'flex', alignItems: 'center', gap: 4 }}>
            🎄 {metadata.currentYear} Trending Now
            {drasiInsights && (
              <StatusBadge label="LIVE" variant="success" />
            )}
          </h4>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            {(drasiInsights && drasiInsights.trending.length > 0 ? drasiInsights.trending : trends.current).slice(0, 5).map((item, i) => (
              <div key={i} style={{
                background: drasiInsights ? 'linear-gradient(135deg, var(--christmas-green), var(--bg-tertiary))' : 'var(--bg-tertiary)',
                padding: '6px 8px',
                borderRadius: 4,
                fontSize: '.8rem',
                border: drasiInsights ? '1px solid var(--christmas-green)' : '1px solid var(--border-dark)',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center'
              }}>
                <span style={{ color: 'var(--text-secondary)' }}>{i + 1}. {item.item}</span>
                <span style={{
                  background: 'var(--christmas-green)',
                  padding: '2px 6px',
                  borderRadius: 3,
                  fontSize: '.7rem',
                  fontWeight: 600,
                  color: 'var(--text-primary)'
                }}>
                  {item.frequency}
                </span>
              </div>
            ))}
          </div>
        </div>

        {/* Last Year */}
        <div>
          <h4 style={{ fontSize: '.9rem', marginBottom: 8, color: 'var(--santa-red)', borderBottom: '2px solid var(--santa-red)', paddingBottom: 4 }}>
            🎅 {metadata.comparisonYear} Trending Then
          </h4>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            {trends.historical.slice(0, 5).map((item, i) => (
              <div key={i} style={{
                background: 'var(--bg-tertiary)',
                padding: '6px 8px',
                borderRadius: 4,
                fontSize: '.8rem',
                border: '1px solid var(--border-dark)',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                opacity: 0.85
              }}>
                <span style={{ color: 'var(--text-secondary)' }}>{i + 1}. {item.item}</span>
                <span style={{
                  background: 'var(--santa-red)',
                  padding: '2px 6px',
                  borderRadius: 3,
                  fontSize: '.7rem',
                  fontWeight: 600,
                  color: 'var(--text-primary)'
                }}>
                  {item.frequency}
                </span>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Insights Section */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 8 }}>
        {/* Returning Favorites */}
        {insights.returningFavorites.length > 0 && (
          <div style={{ background: 'var(--bg-tertiary)', padding: '8px', borderRadius: 6, border: '1px solid var(--border-dark)' }}>
            <div style={{ fontSize: '.75rem', fontWeight: 600, marginBottom: 4, color: 'var(--gold-accent)' }}>
              ⭐ Returning ({insights.returningFavorites.length})
            </div>
            <div style={{ fontSize: '.7rem', color: 'var(--text-muted)', lineHeight: 1.3 }}>
              {insights.returningFavorites.slice(0, 2).join(', ')}
              {insights.returningFavorites.length > 2 && '...'}
            </div>
          </div>
        )}

        {/* New This Year */}
        {insights.newTrends.length > 0 && (
          <div style={{ background: 'var(--bg-tertiary)', padding: '8px', borderRadius: 6, border: '1px solid var(--border-dark)' }}>
            <div style={{ fontSize: '.75rem', fontWeight: 600, marginBottom: 4, color: 'var(--christmas-green)' }}>
              🆕 New ({insights.newTrends.length})
            </div>
            <div style={{ fontSize: '.7rem', color: 'var(--text-muted)', lineHeight: 1.3 }}>
              {insights.newTrends.slice(0, 2).join(', ')}
              {insights.newTrends.length > 2 && '...'}
            </div>
          </div>
        )}

        {/* No Longer Trending */}
        {insights.noLongerTrending.length > 0 && (
          <div style={{ background: 'var(--bg-tertiary)', padding: '8px', borderRadius: 6, border: '1px solid var(--border-dark)' }}>
            <div style={{ fontSize: '.75rem', fontWeight: 600, marginBottom: 4, color: 'var(--text-secondary)', opacity: 0.7 }}>
              📉 Faded ({insights.noLongerTrending.length})
            </div>
            <div style={{ fontSize: '.7rem', color: 'var(--text-muted)', lineHeight: 1.3, opacity: 0.7 }}>
              {insights.noLongerTrending.slice(0, 2).join(', ')}
              {insights.noLongerTrending.length > 2 && '...'}
            </div>
          </div>
        )}
      </div>

      {/* Powered By Footer */}
      <div style={{
        marginTop: 12,
        paddingTop: 8,
        borderTop: '1px solid var(--border-dark)',
        fontSize: '.7rem',
        opacity: 0.5,
        textAlign: 'center',
        color: 'var(--text-muted)'
      }}>
        Historical Data from Cosmos DB • Live Trends from Drasi
      </div>
    </Panel>
  );
};

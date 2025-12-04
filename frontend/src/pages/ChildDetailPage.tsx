import React, { useEffect, useState } from 'react';
import { ChildProfile, Recommendation, getChildProfile, getChildRecommendations, addWishlistItem, WishlistItemInput, createJob } from '../agentClient';
import { useChildRecommendationsLive } from '../hooks/useChildRecommendationsLive';
import { useAgentRun } from '../hooks/useAgentRun';

interface Props { childId: string }

type Tab = 'profile' | 'wishlist' | 'recommendations' | 'logistics' | 'agent';

export const ChildDetailPage: React.FC<Props> = ({ childId }) => {
  const [tab, setTab] = useState<Tab>('profile');
  const [profile, setProfile] = useState<ChildProfile | null>(null);
  const [recs, setRecs] = useState<Recommendation[]>([]);
  const liveRecs = useChildRecommendationsLive(childId) as Recommendation[];
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [wishlistItem, setWishlistItem] = useState<WishlistItemInput>({ toyName: '' });
  const [wishlistStatus, setWishlistStatus] = useState<string | null>(null);
  const { start, cancel, status, events, transcript } = useAgentRun('taskforce');
  const [logisticsResult, setLogisticsResult] = useState<any>(null);
  const [logisticsStatus, setLogisticsStatus] = useState<string | null>(null);
  const [reportStatus, setReportStatus] = useState<string | null>(null);
  const [isGeneratingReport, setIsGeneratingReport] = useState(false);

  async function load() {
    setLoading(true); setError(null);
    try {
      const [p, rRaw] = await Promise.all([
        getChildProfile(childId),
        getChildRecommendations(childId)
      ]);
      // Ensure normalized recommendation objects (defensive in case legacy shapes leak)
      const r = (rRaw || []).map(rec => ({
        id: rec.id,
        childId: rec.childId || childId,
        suggestion: (rec as any).suggestion || (rec as any).Suggestion || rec.suggestion,
        rationale: (rec as any).rationale || (rec as any).Rationale || rec.rationale,
        price: rec.price,
        budgetFit: (rec as any).budgetFit || (rec as any).BudgetFit || rec.budgetFit || 'unknown'
      })) as Recommendation[];
      setProfile(p); setRecs(r);
    } catch (e: any) {
      setError(e.message || 'Load failed');
    } finally { setLoading(false); }
  }

  useEffect(() => { void load(); }, [childId]);

  async function submitWishlist(e: React.FormEvent) {
    e.preventDefault();
    setWishlistStatus(null);
    try {
      const res = await addWishlistItem(childId, wishlistItem);
      setWishlistStatus(`Wishlist item accepted (dedupeKey=${res.dedupeKey})`);
      setWishlistItem({ toyName: '' });
    } catch (err: any) {
      setWishlistStatus(err.message);
    }
  }

  async function generateReport() {
    setIsGeneratingReport(true);
    setReportStatus('Creating job to generate report...');
    try {
      // Create a job which will trigger report generation
      const dedupeKey = `manual-${Date.now()}`;
      await createJob({ 
        childId, 
        dedupeKey,
        schemaVersion: 'v1'
      });
      setReportStatus('✅ Job created! Report will be generated shortly. Check Santa view in 5-10 seconds.');
    } catch (err: any) {
      setReportStatus(`❌ Failed to create job: ${err.message}`);
    } finally {
      setIsGeneratingReport(false);
    }
  }

  return (
    <section>
      <h2>Child: {childId}</h2>
      <nav style={{ display: 'flex', gap: '0.5rem', marginBottom: '0.75rem' }}>
        {(['profile','wishlist','recommendations','logistics','agent'] as Tab[]).map(t => (
          <button key={t} onClick={() => setTab(t)} disabled={tab===t}>{t}</button>
        ))}
      </nav>
      {loading && <p>Loading…</p>}
      {error && <p style={{ color:'red' }}>{error}</p>}
      {tab === 'profile' && (
        <div>
          {profile ? (
            <div>
              <p><strong>Name:</strong> {profile.name ?? 'Unknown'}</p>
              <p><strong>Age:</strong> {profile.age ?? 'Unknown'}</p>
              <p><strong>Preferences:</strong> {profile.preferences?.join(', ') || '(none)'} </p>
              <p><strong>Budget:</strong> {profile.constraints?.budget ?? 'N/A'}</p>
            </div>
          ) : <p>No profile available.</p>}
        </div>
      )}
      {tab === 'wishlist' && (
        <form onSubmit={submitWishlist} style={{ display:'flex', flexDirection:'column', gap:'0.5rem', maxWidth:'320px' }}>
          <input value={wishlistItem.toyName} onChange={e=>setWishlistItem(i=>({ ...i, toyName:e.target.value }))} placeholder="Toy name" required />
          <input value={wishlistItem.category||''} onChange={e=>setWishlistItem(i=>({ ...i, category:e.target.value }))} placeholder="Category" />
          <textarea value={wishlistItem.notes||''} onChange={e=>setWishlistItem(i=>({ ...i, notes:e.target.value }))} placeholder="Notes" />
            <input type="number" value={wishlistItem.budgetLimit?.toString()||''} onChange={e=>setWishlistItem(i=>({ ...i, budgetLimit: e.target.value? Number(e.target.value): undefined }))} placeholder="Budget limit" />
          <button disabled={!wishlistItem.toyName.trim()}>Submit Idea</button>
          {wishlistStatus && <p>{wishlistStatus}</p>}
        </form>
      )}
      {tab === 'recommendations' && (
        <div>
          <div style={{ 
            background: 'linear-gradient(135deg, var(--christmas-green, #2e7d32), var(--santa-red, #c62828))',
            padding: '0.75rem 1rem',
            borderRadius: '8px',
            marginBottom: '1rem',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem'
          }}>
            <span style={{ fontSize: '1.2rem' }}>🤖</span>
            <div>
              <strong style={{ color: 'white' }}>AI-Powered Recommendations</strong>
              <p style={{ margin: 0, fontSize: '0.8rem', color: 'rgba(255,255,255,0.9)' }}>
                Generated by Microsoft Agent Framework + Azure OpenAI • Behavior-aware • Real-time Drasi data
              </p>
            </div>
          </div>
          <p style={{ fontSize:'0.85rem', opacity:0.8, marginBottom: '0.5rem' }}>
            ✨ These recommendations are dynamically generated based on the child's Nice/Naughty status 
            and real-time trending data from Santa's Workshop.
          </p>
          {(liveRecs.length === 0 && recs.length === 0) ? (
            <p style={{ padding: '1rem', background: 'rgba(0,0,0,0.2)', borderRadius: '6px', textAlign: 'center' }}>
              No recommendations yet. Submit a wishlist item to generate AI-powered suggestions!
            </p>
          ) : (
            <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
              {(liveRecs.length > 0 ? liveRecs : recs).map((r, idx) => (
                <li key={r.id} style={{ 
                  background: 'rgba(0,0,0,0.15)', 
                  padding: '0.75rem 1rem', 
                  borderRadius: '6px',
                  marginBottom: '0.5rem',
                  borderLeft: `4px solid ${(r as any).suggestion?.includes('Coal') ? '#ff9800' : '#4caf50'}`
                }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                    <strong style={{ fontSize: '1rem' }}>{(r as any).suggestion}</strong>
                    {(r as any).price && (
                      <span style={{ 
                        background: 'var(--christmas-green, #2e7d32)', 
                        padding: '2px 8px', 
                        borderRadius: '4px',
                        fontSize: '0.8rem'
                      }}>
                        ${(r as any).price}
                      </span>
                    )}
                  </div>
                  <p style={{ margin: '0.5rem 0 0', fontSize: '0.9rem', opacity: 0.9 }}>
                    {(r as any).rationale}
                  </p>
                  {(r as any).budgetFit && (r as any).budgetFit !== 'unknown' && (
                    <span style={{ fontSize: '0.75rem', opacity: 0.7 }}>
                      💰 {(r as any).budgetFit.replace('_', ' ')}
                    </span>
                  )}
                </li>
              ))}
            </ul>
          )}
          <div style={{ display: 'flex', gap: '0.75rem', marginTop: '0.75rem' }}>
            <button onClick={()=>load()} style={{ 
              background: 'var(--santa-red, #c62828)',
              color: 'white',
              border: 'none',
              padding: '0.5rem 1rem',
              borderRadius: '4px',
              cursor: 'pointer'
            }}>
              🔄 Refresh Recommendations
            </button>
            <button 
              onClick={generateReport} 
              disabled={isGeneratingReport}
              style={{ 
                background: isGeneratingReport ? '#666' : 'var(--christmas-gold, #ffd700)',
                color: isGeneratingReport ? '#ccc' : '#000',
                border: 'none',
                padding: '0.5rem 1rem',
                borderRadius: '4px',
                cursor: isGeneratingReport ? 'not-allowed' : 'pointer',
                fontWeight: 600
              }}
            >
              {isGeneratingReport ? '⏳ Generating...' : '📊 Generate Report'}
            </button>
          </div>
          {reportStatus && (
            <p style={{ 
              marginTop: '0.5rem', 
              padding: '0.75rem', 
              background: reportStatus.includes('✅') ? 'rgba(76, 175, 80, 0.2)' : 'rgba(255, 152, 0, 0.2)',
              borderRadius: '4px',
              fontSize: '0.9rem'
            }}>
              {reportStatus}
            </p>
          )}
        </div>
      )}
      {tab === 'logistics' && (
        <div>
          <button onClick={async () => {
            setLogisticsStatus('Running logistics assessment...');
            try {
              const mod = await import('../agentClient');
              const result = await mod.runLogisticsAssessment(childId);
              setLogisticsResult(result);
              setLogisticsStatus('Completed');
            } catch (err: any) {
              setLogisticsStatus(err.message);
            }
          }} disabled={logisticsStatus === 'Running logistics assessment...'}>Run Assessment</button>
          {logisticsStatus && <p>{logisticsStatus}</p>}
          {logisticsResult && <pre style={{ background:'#222', color:'#fff', padding:'0.5rem' }}>{JSON.stringify(logisticsResult, null, 2)}</pre>}
        </div>
      )}
      {tab === 'agent' && (
        <div>
          <div style={{ display:'flex', gap:'0.5rem' }}>
            <button onClick={start} disabled={status==='running'}>Start Run</button>
            <button onClick={cancel} disabled={status!=='running'}>Cancel</button>
          </div>
          <div style={{ marginTop:'0.5rem' }}><strong>Status:</strong> {status}</div>
          <pre style={{ background:'#222', color:'#0f0', padding:'0.5rem', minHeight:'120px' }}>
            {events.map((e,i)=>(<div key={i}>{e.kind==='delta'? e.deltaText : `${e.phase}:${e.kind}`}</div>))}
          </pre>
          <div><strong>Transcript:</strong> {transcript||'(none)'}</div>
        </div>
      )}
    </section>
  );
};

import React, { useState } from 'react';
import { getReport, getNotifications, NotificationDto } from './agentClient';
import { SantaView } from './pages/SantaView';
import { ElfView } from './pages/ElfView';

export const App: React.FC = () => {
  const [childIdInput, setChildIdInput] = useState('');
  const [activeChildId, setActiveChildId] = useState<string>('');
  const [reportMeta, setReportMeta] = useState<any>(null);
  const [reportLoading, setReportLoading] = useState(false);
  const [notifications, setNotifications] = useState<NotificationDto[]>([]);
  const [notifLoading, setNotifLoading] = useState(false);
  const [notifError, setNotifError] = useState<string | null>(null);

  async function loadReport(id: string) {
    setReportLoading(true);
    try { 
      const meta = await getReport(id); 
      setReportMeta(meta); 
      if (!meta) {
        console.log(`No report found for ${id}. User may need to generate one.`);
      }
    } catch (err) { 
      console.error('Failed to load report:', err);
      setReportMeta(null); 
    } finally {
      setReportLoading(false);
    }
  }

  function handleOpenChild(id: string) {
    setActiveChildId(id);
    setChildIdInput(id); // Also update the input field for consistency
    void loadReport(id);
  }

  async function loadNotifications() {
    setNotifLoading(true); setNotifError(null);
    try {
      const data = await getNotifications();
      setNotifications(data);
    } catch (e:any) { setNotifError(e.message || 'Failed to load'); }
    finally { setNotifLoading(false); }
  }

  React.useEffect(()=>{ void loadNotifications(); const t = setInterval(()=>void loadNotifications(), 15000); return ()=>clearInterval(t); },[]);

  return (
    <div style={{ fontFamily: 'system-ui', minHeight: '100vh', background: 'var(--winter-sky)', color: 'var(--text-primary)' }}>
      <header style={{ padding: '1rem 2rem', display:'flex', alignItems:'center', justifyContent:'space-between', background:'var(--bg-secondary)', boxShadow:'0 2px 6px rgba(0,0,0,0.4)', borderBottom: '2px solid var(--santa-red)' }}>
        <h1 style={{ margin:0, fontSize:'1.75rem', color: 'var(--text-primary)' }}>
          🎅 Santa's Workshop Dashboard
          <span className="ornament-dot red" style={{ marginLeft: 8 }}></span>
          <span className="ornament-dot green"></span>
          <span className="ornament-dot gold"></span>
        </h1>
        <div style={{ opacity:.7, fontSize:'.9rem' }}>Santa view • Elf view</div>
      </header>
      <main style={{
        display:'grid',
        gap:'1.25rem',
        padding:'1rem 2rem',
        gridTemplateColumns:'minmax(320px, 1.2fr) minmax(420px, 2fr)'
      }}>
        <div>
          <SantaView
            childIdInput={childIdInput}
            setChildIdInput={setChildIdInput}
            onOpenChild={handleOpenChild}
            reportMeta={reportMeta}
            reportLoading={reportLoading}
            notifications={notifications}
            notifLoading={notifLoading}
            notifError={notifError}
            onRefreshNotifications={()=>void loadNotifications()}
            onRefreshReport={() => activeChildId && void loadReport(activeChildId)}
          />
        </div>
        <div>
          <ElfView activeChildId={activeChildId} onChildSelected={handleOpenChild} />
        </div>
      </main>
      <footer style={{ padding:'0.75rem 2rem', textAlign:'center', fontSize:'0.75rem', opacity:0.6 }}>© {new Date().getFullYear()} Santa Workshop • Demo environment</footer>
    </div>
  );
};

export default App;

import React, { useEffect, useState } from 'react';
import { API_URL } from '../config';

export const ElfAgentsStatusPage: React.FC = () => {
  const [data, setData] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(()=>{
    fetch(`${API_URL}/api/v1/elf-agents/status`).then(r=> r.ok? r.json(): Promise.reject(r.statusText))
      .then(setData).catch(e=> setError(String(e)));
  }, []);
  return (
    <section>
      <h2>Elf Agents Status</h2>
      {error && <p style={{ color:'red' }}>{error}</p>}
      {!error && !data && <p>Loading…</p>}
      {data && <pre style={{ background:'#222', color:'#fff', padding:'0.5rem' }}>{JSON.stringify(data, null, 2)}</pre>}
    </section>
  );
};

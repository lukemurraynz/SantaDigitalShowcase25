import React, { useState } from 'react';
import { createChild } from '../agentClient';
import { useNavigate } from 'react-router-dom';

export const ChildrenDashboardPage: React.FC = () => {
  const [newChildId, setNewChildId] = useState('');
  const [status, setStatus] = useState<string | null>(null);
  const navigate = useNavigate();

  async function add(e: React.FormEvent) {
    e.preventDefault();
    if (!newChildId.trim()) return;
    try {
      await createChild(newChildId.trim());
      setStatus('Child registered');
      navigate(`/children/${encodeURIComponent(newChildId.trim())}`);
    } catch (err: any) {
      setStatus(err.message);
    }
  }

  return (
    <section>
      <h2>Children</h2>
      <form onSubmit={add} style={{ display: 'flex', gap: '0.5rem' }}>
        <input value={newChildId} onChange={e => setNewChildId(e.target.value)} placeholder="child id" />
        <button disabled={!newChildId.trim()}>Add Child</button>
      </form>
      {status && <p>{status}</p>}
      <p>Enter a child id to create and jump to its detail view.</p>
    </section>
  );
};

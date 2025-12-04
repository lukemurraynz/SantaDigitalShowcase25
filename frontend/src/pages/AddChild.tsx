import React, { useState } from 'react';
import { createChild } from '../agentClient';

export const AddChild: React.FC = () => {
  const [childId, setChildId] = useState('');
  const [status, setStatus] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setStatus(null);
    try {
      const res = await createChild(childId.trim());
      setStatus(`Created: ${res.childId}`);
    } catch (err: any) {
      setStatus(err.message);
    }
  };


  return (
    <div>
      <h2>Add Child</h2>
      <form onSubmit={submit}>
        <input value={childId} onChange={e => setChildId(e.target.value)} placeholder="child id" />
        <button disabled={!childId.trim()}>Create</button>
      </form>
      {status && <p>{status}</p>}
    </div>
  );
};

import React, { useEffect, useState } from 'react';
import { getReport } from '../agentClient';
import { useAgentRun } from '../hooks/useAgentRun';

interface LogEntry { ts: number; text: string }

export const RunAgent: React.FC = () => {
  const [agentId, setAgentId] = useState('taskforce');
  const [childId, setChildId] = useState('child-123');
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [reportMeta, setReportMeta] = useState<any>(null);
  const { start, cancel, status, events, transcript } = useAgentRun(agentId);

  const append = (text: string) => setLogs(l => [...l, { ts: Date.now(), text }]);

  useEffect(() => {
    // When run finishes, fetch report metadata
    if (status === 'finished') {
      getReport(childId).then(meta => meta && setReportMeta(meta)).catch(() => {});
    }
  }, [status, childId]);

  return (
    <div>
      <h2>Run Agent</h2>
      <div style={{ display: 'flex', gap: '0.5rem' }}>
        <input value={agentId} onChange={e => setAgentId(e.target.value)} />
        <input value={childId} onChange={e => setChildId(e.target.value)} />
        <button onClick={() => { append('Starting run...'); start(); }} disabled={status === 'running'}>Start</button>
        <button onClick={() => { cancel(); append('Cancelled.'); }} disabled={status !== 'running'}>Cancel</button>
      </div>
      <pre style={{ background: '#222', color: '#0f0', padding: '0.5rem', minHeight: '120px' }}>
        {events.map((e, i) => <div key={i}>{e.kind === 'delta' ? e.deltaText : e.kind}</div>)}
      </pre>
      <div style={{ marginTop: '0.5rem' }}>
        <strong>Status:</strong> {status} <br />
        <strong>Transcript:</strong> {transcript || '(none yet)'}
      </div>
      {reportMeta && (
        <div>
          <h3>Report Metadata</h3>
          <code>{JSON.stringify(reportMeta)}</code>
        </div>
      )}
    </div>
  );
};


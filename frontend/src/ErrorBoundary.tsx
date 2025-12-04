import React from 'react';

interface State { error: Error | null }

export class ErrorBoundary extends React.Component<React.PropsWithChildren, State> {
  state: State = { error: null };
  static getDerivedStateFromError(error: Error): State { return { error }; }
  componentDidCatch(error: Error, info: any) {
    // Error is captured in state and displayed to user
    // Browser console will still show the error for debugging
  }
  render() {
    if (this.state.error) {
      return (
        <div style={{ padding:'2rem', fontFamily:'system-ui' }}>
          <h1 style={{ color:'#f66' }}>Santa Dashboard Error</h1>
          <p>Something went wrong rendering the dashboard.</p>
          <pre style={{ whiteSpace:'pre-wrap', background:'#300', color:'#faa', padding:'0.75rem', borderRadius:4 }}>{this.state.error.message}</pre>
          <button onClick={()=>window.location.reload()} style={{ padding:'0.5rem 1rem', background:'#2d6cdf', color:'#fff', border:'none', borderRadius:4 }}>Reload</button>
        </div>
      );
    }
    return this.props.children;
  }
}
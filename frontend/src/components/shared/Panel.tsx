import React from 'react';

export interface PanelProps {
  children: React.ReactNode;
  style?: React.CSSProperties;
  maxHeight?: string;
}

/**
 * A consistent panel container used across dashboard components.
 * Provides standard styling for background, padding, border, and shadow.
 */
export const Panel: React.FC<PanelProps> = ({ children, style, maxHeight }) => {
  return (
    <div style={{
      background: 'var(--bg-secondary)',
      padding: '1rem',
      borderRadius: 8,
      boxShadow: '0 2px 4px rgba(0,0,0,0.5)',
      border: '1px solid var(--border-medium)',
      ...(maxHeight && { maxHeight, display: 'flex', flexDirection: 'column' }),
      ...style,
    }}>
      {children}
    </div>
  );
};

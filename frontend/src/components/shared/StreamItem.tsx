import React from 'react';

export interface StreamItemProps {
  children: React.ReactNode;
  style?: React.CSSProperties;
}

/**
 * A single item in a stream list with consistent styling.
 */
export const StreamItem: React.FC<StreamItemProps> = ({ children, style }) => {
  return (
    <div style={{
      display: 'flex',
      justifyContent: 'space-between',
      padding: '0.25rem 0',
      borderBottom: '1px solid var(--border-light)',
      color: 'var(--text-primary)',
      ...style,
    }}>
      {children}
    </div>
  );
};

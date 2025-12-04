import React from 'react';

export type StatusBadgeVariant = 'success' | 'warning' | 'error' | 'info' | 'neutral';

export interface StatusBadgeProps {
  label: string;
  variant?: StatusBadgeVariant;
}

const getVariantStyles = (variant: StatusBadgeVariant): React.CSSProperties => {
  switch (variant) {
    case 'success':
      return { background: 'var(--christmas-green)' };
    case 'warning':
      return { background: 'var(--christmas-gold)' };
    case 'error':
      return { background: 'var(--santa-red)' };
    case 'info':
      return { background: 'var(--christmas-red)' };
    case 'neutral':
    default:
      return { background: 'var(--text-muted)' };
  }
};

/**
 * A small status badge for showing connection status or other indicators.
 */
export const StatusBadge: React.FC<StatusBadgeProps> = ({ label, variant = 'neutral' }) => {
  return (
    <span style={{
      fontSize: '0.75rem',
      padding: '4px 8px',
      borderRadius: 4,
      fontWeight: 600,
      color: 'var(--text-primary)',
      ...getVariantStyles(variant),
    }}>
      {label}
    </span>
  );
};

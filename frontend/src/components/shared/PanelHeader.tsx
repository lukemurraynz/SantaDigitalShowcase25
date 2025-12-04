import React from 'react';
import { StatusBadge, StatusBadgeVariant } from './StatusBadge';

export interface PanelHeaderProps {
  title: string;
  icon?: string;
  badge?: {
    label: string;
    variant: StatusBadgeVariant;
  };
}

/**
 * A consistent panel header with optional icon and status badge.
 */
export const PanelHeader: React.FC<PanelHeaderProps> = ({ title, icon, badge }) => {
  return (
    <h3 style={{
      marginTop: 0,
      display: 'flex',
      alignItems: 'center',
      gap: 8,
      justifyContent: 'space-between',
      color: 'var(--text-primary)',
    }}>
      <span>{icon && `${icon} `}{title}</span>
      {badge && <StatusBadge label={badge.label} variant={badge.variant} />}
    </h3>
  );
};

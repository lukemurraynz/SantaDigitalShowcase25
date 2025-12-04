import React from 'react';

export interface StreamListProps {
  children: React.ReactNode;
  title?: string;
  titleColor?: string;
  titleIcon?: string;
  maxHeight?: string;
  emptyMessage?: string;
  isEmpty?: boolean;
}

/**
 * A scrollable list container for displaying streams of data items.
 */
export const StreamList: React.FC<StreamListProps> = ({
  children,
  title,
  titleColor = 'var(--text-primary)',
  titleIcon,
  maxHeight = '150px',
  emptyMessage = 'No items to display',
  isEmpty = false,
}) => {
  return (
    <div style={{ marginBottom: '1rem' }}>
      {title && (
        <h4 style={{
          color: titleColor,
          marginBottom: '0.5rem',
          fontSize: '0.9rem',
        }}>
          {titleIcon && `${titleIcon} `}{title}
        </h4>
      )}
      <div style={{
        background: 'var(--bg-primary)',
        padding: '0.75rem',
        borderRadius: 6,
        maxHeight,
        overflowY: 'auto',
      }}>
        {isEmpty ? (
          <div style={{
            textAlign: 'center',
            padding: '1rem',
            color: 'var(--text-muted)',
            fontSize: '0.9rem',
          }}>
            {emptyMessage}
          </div>
        ) : children}
      </div>
    </div>
  );
};

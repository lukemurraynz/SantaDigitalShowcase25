import React from 'react';

export type ElfStatus = 'idle' | 'thinking' | 'working' | 'complete' | 'error';

interface ElfAvatarProps {
  status: ElfStatus;
  agentType?: 'profile' | 'recommendation' | 'logistics' | 'santa';
  size?: number;
  showLabel?: boolean;
}

const getElfColor = (agentType: string) => {
  switch (agentType) {
    case 'profile': return 'var(--elf-profile)';
    case 'recommendation': return 'var(--elf-recommendation)';
    case 'logistics': return 'var(--elf-logistics)';
    case 'santa': return 'var(--santa-accent)';
    default: return 'var(--christmas-green)';
  }
};

const getStatusAnimation = (status: ElfStatus) => {
  switch (status) {
    case 'idle': return 'elfBob 3s ease-in-out infinite';
    case 'thinking': return 'elfBob 1.5s ease-in-out infinite';
    case 'working': return 'elfWork 0.8s ease-in-out infinite';
    case 'complete': return 'elfCelebrate 1s ease-in-out';
    case 'error': return 'none';
    default: return 'elfBob 3s ease-in-out infinite';
  }
};

const getStatusEmoji = (status: ElfStatus, agentType?: string) => {
  if (agentType === 'santa') return '🎅';
  
  switch (status) {
    case 'idle': return '🧝';
    case 'thinking': return '🤔';
    case 'working': return '⚙️';
    case 'complete': return '✨';
    case 'error': return '😰';
    default: return '🧝';
  }
};

const getStatusLabel = (status: ElfStatus) => {
  switch (status) {
    case 'idle': return 'Ready';
    case 'thinking': return 'Thinking...';
    case 'working': return 'Working...';
    case 'complete': return 'Complete!';
    case 'error': return 'Error';
    default: return 'Ready';
  }
};

export const ElfAvatar: React.FC<ElfAvatarProps> = ({ 
  status, 
  agentType = 'profile', 
  size = 48,
  showLabel = false 
}) => {
  const color = getElfColor(agentType);
  const animation = getStatusAnimation(status);
  const emoji = getStatusEmoji(status, agentType);
  const label = getStatusLabel(status);

  return (
    <div style={{ 
      display: 'inline-flex', 
      flexDirection: 'column', 
      alignItems: 'center', 
      gap: 4 
    }}>
      <div
        style={{
          width: size,
          height: size,
          borderRadius: '50%',
          background: color,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: size * 0.6,
          animation,
          border: `2px solid ${status === 'complete' ? 'var(--gold-accent)' : 'transparent'}`,
          boxShadow: status === 'complete' 
            ? '0 0 12px var(--gold-accent)' 
            : status === 'error'
            ? '0 0 8px var(--status-error)'
            : 'none',
          transition: 'all 0.3s ease',
          position: 'relative',
        }}
      >
        {emoji}
        
        {/* Thinking dots indicator */}
        {status === 'thinking' && (
          <div style={{
            position: 'absolute',
            bottom: -8,
            display: 'flex',
            gap: 2,
          }}>
            {[0, 1, 2].map(i => (
              <div
                key={i}
                style={{
                  width: 4,
                  height: 4,
                  borderRadius: '50%',
                  background: 'var(--text-primary)',
                  animation: 'bounce 1.4s ease-in-out infinite',
                  animationDelay: `${i * 0.16}s`,
                }}
              />
            ))}
          </div>
        )}
      </div>
      
      {showLabel && (
        <div style={{
          fontSize: '0.7rem',
          color: 'var(--text-muted)',
          fontWeight: 500,
          textAlign: 'center',
        }}>
          {label}
        </div>
      )}
    </div>
  );
};

export default ElfAvatar;

const LOG_LEVEL = (import.meta.env.VITE_LOG_LEVEL as 'debug' | 'info' | 'warn' | 'error' | undefined) || 'warn';

export const logger = {
  debug: (...args: any[]) => {
    if (LOG_LEVEL === 'debug') console.log(...args);
  },
  info: (...args: any[]) => {
    if (['debug', 'info'].includes(LOG_LEVEL)) console.log(...args);
  },
  warn: (...args: any[]) => {
    if (['debug', 'info', 'warn'].includes(LOG_LEVEL)) console.warn(...args);
  },
  error: (...args: any[]) => console.error(...args)
};

export const BEHAVIOR_KEYWORDS = [
  'behavior', 'behave', 'chores', 'naughty', 'nice list', 'improve',
  'BEHAVIOR REPORT', 'helpful', 'kind', 'better', 'try to', 'will continue'
] as const;

export function isBehaviorMessage(text: string): boolean {
  if (!text) return false;
  const lowerText = text.toLowerCase();
  return BEHAVIOR_KEYWORDS.some(keyword => lowerText.includes(keyword.toLowerCase()));
}

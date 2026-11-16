/** Splits a sentence around the first case-insensitive occurrence of `word`, to show the word in bold in context. */
export function highlight(sentence: string, word: string): [before: string, match: string, after: string] {
  const at = sentence.toLocaleLowerCase('de').indexOf(word.toLocaleLowerCase('de'))
  if (word === '' || at < 0) return [sentence, '', '']
  return [sentence.slice(0, at), sentence.slice(at, at + word.length), sentence.slice(at + word.length)]
}

/** "due now", "in 3 days", for a card's next review. */
export function dueLabel(dueAt: string, now = Date.now()): string {
  const ms = new Date(dueAt).getTime() - now
  if (ms <= 0) return 'Due now'
  const minutes = Math.round(ms / 60000)
  if (minutes < 60) return `Due in ${minutes} min`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `Due in ${hours} h`
  const days = Math.round(hours / 24)
  return days === 1 ? 'Due tomorrow' : `Due in ${days} days`
}

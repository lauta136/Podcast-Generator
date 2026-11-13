import type { TranscriptSection, TranscriptSegment } from '../api/types'

export interface TimedSegment extends TranscriptSegment {
  startSec: number
  endSec: number
  sectionIndex: number
}

/** Flattens sections into one timeline. Segments without timings (audio not generated) are dropped. */
export function timeline(sections: TranscriptSection[]): TimedSegment[] {
  return sections.flatMap((section, sectionIndex) =>
    section.segments
      .filter((s): s is TranscriptSegment & { startSec: number; endSec: number } => s.startSec !== null && s.endSec !== null)
      .map((s) => ({ ...s, sectionIndex })),
  )
}

/** Index of the segment playing at `time` (binary search), or -1 before the first one. */
export function activeSegmentIndex(segments: TimedSegment[], time: number): number {
  let lo = 0
  let hi = segments.length - 1
  let found = -1
  while (lo <= hi) {
    const mid = (lo + hi) >> 1
    if (segments[mid].startSec <= time) {
      found = mid
      lo = mid + 1
    } else {
      hi = mid - 1
    }
  }
  return found
}

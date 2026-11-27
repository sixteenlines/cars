import { useEffect, useRef } from 'react'

/** "⌘V" on Mac, "Ctrl+V" elsewhere — for hints next to upload buttons. */
export const pasteKey = () => (/Mac|iPhone|iPad/.test(navigator.userAgent) ? '⌘V' : 'Ctrl+V')

export const isImage = (f: File) => f.type.startsWith('image/') || /\.(heic|heif)$/i.test(f.name)
export const isBillFile = (f: File) => isImage(f) || f.type === 'application/pdf' || /\.pdf$/i.test(f.name)

/**
 * Files pasted anywhere on the page (screenshot, image copied in a browser, file copied in Finder) go to `onFiles`.
 * Text pastes into inputs are left alone: only clipboard *files* that pass `accept` are taken.
 */
export function usePasteFiles(onFiles: (files: File[]) => void, accept: (f: File) => boolean, enabled = true) {
  const handler = useRef(onFiles)
  handler.current = onFiles
  const filter = useRef(accept)
  filter.current = accept

  useEffect(() => {
    if (!enabled) return
    const onPaste = (e: ClipboardEvent) => {
      const files = Array.from(e.clipboardData?.files ?? []).filter(filter.current)
      if (!files.length) return
      e.preventDefault()
      handler.current(files)
    }
    document.addEventListener('paste', onPaste)
    return () => document.removeEventListener('paste', onPaste)
  }, [enabled])
}

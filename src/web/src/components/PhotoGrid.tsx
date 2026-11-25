import { ChevronLeft, ChevronRight, ImagePlus, Star, Trash2, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { photoUrl, type Photo } from '../api'
import { ConfirmButton, Spinner } from './ui'
import { t } from '../i18n'

export interface PendingPhoto { key: string; file: File; preview: string }

export const toPending = (files: FileList | File[]): PendingPhoto[] =>
  Array.from(files)
    .filter(f => f.type.startsWith('image/') || /\.(heic|heif)$/i.test(f.name))
    .map(f => ({ key: `${f.name}-${f.size}-${f.lastModified}-${Math.random()}`, file: f, preview: URL.createObjectURL(f) }))

interface Props {
  photos: Photo[]
  pending?: PendingPhoto[]
  uploading?: boolean
  coverId?: number | null
  onAdd: (files: PendingPhoto[]) => void
  onRemovePending?: (key: string) => void
  onDelete?: (photo: Photo) => void
  onSetCover?: (photo: Photo) => void
  addLabel?: string
}

/** Photo grid with an add tile: tap → iOS offers library/camera/files; desktop also supports drag & drop. */
export function PhotoGrid({ photos, pending = [], uploading, coverId, onAdd, onRemovePending, onDelete, onSetCover, addLabel = t('Add photos', 'Fotos hinzufügen') }: Props) {
  const input = useRef<HTMLInputElement>(null)
  const [over, setOver] = useState(false)
  const [open, setOpen] = useState<number | null>(null)

  const handleFiles = (files: FileList | null) => {
    if (files?.length) onAdd(toPending(files))
  }

  return (
    <div
      onDragOver={e => { e.preventDefault(); setOver(true) }}
      onDragLeave={() => setOver(false)}
      onDrop={e => { e.preventDefault(); setOver(false); handleFiles(e.dataTransfer.files) }}
    >
      <div className="photo-grid">
        <button type="button" className={`drop ${over ? 'over' : ''}`} onClick={() => input.current?.click()}>
          {uploading ? <Spinner /> : <ImagePlus size={26} />}
          {uploading ? t('Uploading…', 'Lädt hoch…') : addLabel}
        </button>
        {pending.map(p => (
          <div key={p.key} className="ph pending">
            <img src={p.preview} alt="" onError={e => ((e.target as HTMLImageElement).style.visibility = 'hidden')} />
            {onRemovePending && !uploading && (
              <button type="button" className="x" aria-label={t('Remove', 'Entfernen')} onClick={() => onRemovePending(p.key)}><X size={16} /></button>
            )}
          </div>
        ))}
        {photos.map((p, i) => (
          <button type="button" key={p.id} className="ph" onClick={() => setOpen(i)}>
            <img src={photoUrl(p.id)} alt={p.caption ?? ''} loading="lazy" />
            {coverId === p.id && <span className="cover-badge">{t('Cover', 'Titelbild')}</span>}
          </button>
        ))}
      </div>
      <input ref={input} type="file" accept="image/*,.heic,.heif" multiple hidden
        onChange={e => { handleFiles(e.target.files); e.target.value = '' }} />
      {open !== null && photos[open] && (
        <Lightbox photos={photos} index={open} onIndex={setOpen} onClose={() => setOpen(null)}
          coverId={coverId} onDelete={onDelete} onSetCover={onSetCover} />
      )}
    </div>
  )
}

function Lightbox({ photos, index, onIndex, onClose, coverId, onDelete, onSetCover }: {
  photos: Photo[]; index: number; onIndex: (i: number) => void; onClose: () => void
  coverId?: number | null; onDelete?: (p: Photo) => void; onSetCover?: (p: Photo) => void
}) {
  const photo = photos[index]
  const touchX = useRef<number | null>(null)
  const prev = () => onIndex((index - 1 + photos.length) % photos.length)
  const next = () => onIndex((index + 1) % photos.length)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
      if (e.key === 'ArrowLeft') prev()
      if (e.key === 'ArrowRight') next()
    }
    window.addEventListener('keydown', onKey)
    document.body.style.overflow = 'hidden'
    return () => { window.removeEventListener('keydown', onKey); document.body.style.overflow = '' }
  })

  return (
    <div className="lightbox" role="dialog" aria-modal="true"
      onTouchStart={e => (touchX.current = e.touches[0].clientX)}
      onTouchEnd={e => {
        if (touchX.current === null) return
        const dx = e.changedTouches[0].clientX - touchX.current
        if (Math.abs(dx) > 50) (dx > 0 ? prev : next)()
        touchX.current = null
      }}>
      <div className="bar">
        <button className="icon-btn" aria-label={t('Close', 'Schließen')} onClick={onClose}><X size={24} /></button>
        <span className="grow" style={{ fontSize: '.9rem', opacity: .8 }}>{index + 1} / {photos.length}</span>
        {onSetCover && coverId !== photo.id && (
          <button className="btn" onClick={() => onSetCover(photo)}><Star size={16} /> {t('Cover', 'Titelbild')}</button>
        )}
        {onDelete && (
          <ConfirmButton className="btn" ariaLabel={t('Delete photo', 'Foto löschen')} onConfirm={() => { onDelete(photo); onClose() }}
            confirmLabel={<><Trash2 size={16} /> {t('Delete?', 'Löschen?')}</>}><Trash2 size={16} /></ConfirmButton>
        )}
      </div>
      <img src={photoUrl(photo.id, 'full')} alt={photo.caption ?? ''} />
      {photos.length > 1 && (
        <div className="bar" style={{ justifyContent: 'space-between', paddingBottom: 'calc(12px + env(safe-area-inset-bottom))' }}>
          <button className="icon-btn" aria-label={t('Previous', 'Zurück')} onClick={prev}><ChevronLeft size={28} /></button>
          <button className="icon-btn" aria-label={t('Next', 'Weiter')} onClick={next}><ChevronRight size={28} /></button>
        </div>
      )}
    </div>
  )
}

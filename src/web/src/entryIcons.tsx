import {
  BatteryFull, Car, ClipboardCheck, Flag, Droplet, Fuel, Gauge, Hammer, KeyRound, Landmark, Lightbulb, Receipt,
  ShieldCheck, Snowflake, Sparkles, Speaker, Thermometer, Users, Wrench, Zap,
} from 'lucide-react'
import type { ComponentType, SVGProps } from 'react'
import type { EntryKind } from './api'
import { categoryFor, categoryLabel, KIND_HUE } from './categories'
import { kindLabel } from './i18n'

type IconProps = { size?: number; className?: string }
type IconComp = ComponentType<IconProps>

// ---- Custom car-part icons (lucide style: 24×24, 2px stroke, round caps) ----------------------
const svg = (children: React.ReactNode) =>
  function CustomIcon({ size = 24, className }: IconProps) {
    const props: SVGProps<SVGSVGElement> = {
      width: size, height: size, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor',
      strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round', className, 'aria-hidden': true,
    }
    return <svg {...props}>{children}</svg>
  }

const WheelIcon = svg(<>
  <circle cx="12" cy="12" r="10" />
  <circle cx="12" cy="12" r="6.5" />
  <circle cx="12" cy="12" r="1.5" />
  <path d="M12 5.5v5M18.2 10l-4.8 1.5M15.8 17.3l-3-4.1M8.2 17.3l3-4.1M5.8 10l4.8 1.5" />
</>)

const BrakeIcon = svg(<>
  <circle cx="11" cy="13" r="8.5" />
  <circle cx="11" cy="13" r="3" />
  <circle cx="11" cy="7.5" r=".6" fill="currentColor" />
  <circle cx="16" cy="15.5" r=".6" fill="currentColor" />
  <circle cx="6" cy="15.5" r=".6" fill="currentColor" />
  <path d="M15.5 3.2a10.5 10.5 0 0 1 5.3 5.6l-2.6 1a7.6 7.6 0 0 0-3.8-4z" fill="currentColor" />
</>)

const ExhaustIcon = svg(<>
  <rect x="2" y="9" width="11" height="7" rx="3.5" />
  <path d="M13 12.5h4.5" />
  <path d="M17.5 10.5v4" />
  <circle cx="21" cy="9" r="1" />
  <circle cx="20.5" cy="15.5" r=".8" />
</>)

const SuspensionIcon = svg(<>
  <path d="M8 3h8M8 21h8" />
  <path d="M9 5.5l6 2-6 2.2 6 2.1-6 2.2 6 2.1-6 2.4" />
</>)

const EngineIcon = svg(<>
  <rect x="6" y="3" width="12" height="9" rx="1.5" />
  <path d="M6 6h12M6 8.5h12" />
  <path d="M12 12v5.5" />
  <circle cx="12" cy="19.5" r="2" />
</>)

const DrivetrainIcon = svg(<>
  <circle cx="7" cy="8" r="4" />
  <circle cx="17" cy="16" r="4" />
  <circle cx="7" cy="8" r="1" />
  <circle cx="17" cy="16" r="1" />
  <path d="M10 11l4 2" />
  <path d="M7 2.5V4M7 12v1.5M1.5 8H3M11 8h1.5M17 10.5V12M17 20v1.5M11.5 16H13M21 16h1.5" />
</>)

const SteeringIcon = svg(<>
  <circle cx="12" cy="12" r="9.5" />
  <circle cx="12" cy="12" r="2" />
  <path d="M12 14v7.5M10 11.5L3 9.5M14 11.5l7-2" />
</>)

const SeatIcon = svg(<>
  <path d="M7 3h3.5a2 2 0 0 1 2 2.2L11.6 14H17a2 2 0 0 1 2 2v2H8a2 2 0 0 1-2-2.2l1-11A2 2 0 0 1 7 3z" />
  <path d="M9 18v3M16 18v3" />
</>)

const ICONS: Record<string, IconComp> = {
  inspection: ClipboardCheck, insurance: ShieldCheck, registration: Landmark, battery: BatteryFull,
  lighting: Lightbulb, locks: KeyRound, exterior: Car, brake: BrakeIcon, exhaust: ExhaustIcon, wheel: WheelIcon,
  steering: SteeringIcon, suspension: SuspensionIcon, drivetrain: DrivetrainIcon, oil: Droplet, engine: EngineIcon,
  ac: Snowflake, cooling: Thermometer, fuel: Fuel, audio: Speaker, electrical: Zap, interior: SeatIcon,
  paint: Sparkles, tools: Hammer, labour: Wrench, membership: Users,
}
const KIND_ICON: Record<EntryKind, IconComp> = { Mod: Gauge, Maintenance: Wrench, Expense: Receipt, Event: Flag }

export function iconFor(entry: { kind: EntryKind; title: string; tags: string[] }) {
  // Events always get the flag: their titles ("Trackday Hockenheim") shouldn't turn into a part icon.
  const cat = entry.kind === 'Event' ? null : categoryFor(entry)
  if (cat && ICONS[cat.key]) return { icon: ICONS[cat.key], hue: cat.hue, label: categoryLabel(cat) }
  return { icon: KIND_ICON[entry.kind], hue: KIND_HUE[entry.kind], label: kindLabel(entry.kind) }
}

export function EntryIcon({ entry, size = 22, large }: { entry: { kind: EntryKind; title: string; tags: string[] }; size?: number; large?: boolean }) {
  const { icon: Icon, hue, label } = iconFor(entry)
  return (
    <span className={large ? 'entry-icon lg' : 'entry-icon'} style={{ '--hue': hue } as React.CSSProperties} title={label}>
      <Icon size={size} />
    </span>
  )
}

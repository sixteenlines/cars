import type { EntryKind } from './api'
import { isDe } from './i18n'

// ---- Categories in priority order (first match wins) -----------------------------------------
// Keyword rules: "=word" must match a whole word; keywords with ≥5 chars match anywhere
// (German compounds: "Hinterachsdifferential"); shorter ones match the start of a word.
export interface Category { key: string; label: string; de: string; hue: number; keywords: string[] }

export const categoryLabel = (c: Category) => (isDe() ? c.de : c.label)

export const CATEGORIES: Category[] = [
  { key: 'inspection', label: 'Inspection', de: 'TÜV & Prüfung', hue: 200,
    keywords: ['inspection', 'tüv', '=hu', '=au', 'dekra', 'eintragung', 'gutachten', 'abnahme'] },
  { key: 'insurance', label: 'Insurance', de: 'Versicherung', hue: 210,
    keywords: ['insurance', 'versicherung', 'haftpflicht', 'kasko'] },
  { key: 'registration', label: 'Tax & registration', de: 'Steuer & Zulassung', hue: 220,
    keywords: ['registration', 'zulassung', 'anmeldung', 'kennzeichen', 'nummernschild', 'plate', '=steuer', 'kfz-steuer', '=tax'] },
  { key: 'battery', label: 'Battery', de: 'Batterie', hue: 95,
    keywords: ['batterie', 'battery', 'varta'] },
  { key: 'lighting', label: 'Lighting', de: 'Beleuchtung', hue: 48,
    keywords: ['=light', 'lights', 'lighting', '=licht', 'scheinwerfer', 'headlight', 'nebel', 'blinker', 'nightbreaker', 'xenon', 'leucht', 'lampe', '=led', '=leds', '=bulb'] },
  { key: 'locks', label: 'Locks & keys', de: 'Schlösser & Schlüssel', hue: 35,
    keywords: ['schloss', '=lock', '=key', '=keys', 'autoschlüssel', 'ersatzschlüssel', 'fernbedienung'] },
  { key: 'exterior', label: 'Exterior', de: 'Karosserie & Außen', hue: 260,
    keywords: ['exterior', 'außen', 'stoßstange', 'bumper', 'spoiler', 'lippe', 'niere', 'kidney', 'spiegel', 'mirror',
      'motorhaube', 'haube', '=hood', 'diffusor', 'splitter', 'folie', '=wrap', 'zierstäbe', 'zierleiste', 'karosserie',
      'heckklappe', 'gasdruckfeder', 'tür', 'door', 'kotflügel', 'fender', 'grill', 'gitter', 'emblem', 'scheibenwisch'] },
  { key: 'brake', label: 'Brakes', de: 'Bremsen', hue: 0,
    keywords: ['brake', 'brems', 'caliper', 'sattel', 'belag', 'beläge', '=pads'] },
  { key: 'exhaust', label: 'Exhaust', de: 'Auspuff', hue: 15,
    keywords: ['exhaust', 'auspuff', 'downpipe', 'schalldämpfer', '=esd', '=msd', 'catback', '=kat', 'vorkat', 'lambda', 'abgas'] },
  { key: 'wheel', label: 'Wheels & tyres', de: 'Räder & Reifen', hue: 230,
    keywords: ['wheel', 'tyre', '=tire', 'tires', 'felge', 'reifen', 'räder', 'radschraub', '=rad', 'rims', 'conti', 'michelin', 'pirelli', 'bridgestone', 'alpin', 'pilot sport'] },
  { key: 'steering', label: 'Steering', de: 'Lenkung', hue: 280,
    keywords: ['lenkrad', 'steering', 'lenkgetriebe', 'servo'] },
  { key: 'suspension', label: 'Suspension', de: 'Fahrwerk', hue: 300,
    keywords: ['suspension', 'fahrwerk', 'coilover', 'gewinde', 'dämpfer', 'shock', 'feder', 'spring', 'querlenker', 'lenker',
      'spurstange', 'koppelstange', 'stabi', 'achsvermessung', 'alignment', 'domlager', '=kw', 'bilstein', 'eibach', 'h&r', 'ta technix', 'silentlager', 'hydrolager', 'hyrdolager'] },
  { key: 'drivetrain', label: 'Drivetrain', de: 'Antrieb', hue: 180,
    keywords: ['drivetrain', 'getriebe', 'gearbox', 'kupplung', 'clutch', 'differential', '=diff', 'difföl', '=lsd', 'sperre', 'antrieb', 'kardan', '=dkg', '=dct', 'schwungrad', 'flywheel'] },
  { key: 'oil', label: 'Oil & fluids', de: 'Öl & Flüssigkeiten', hue: 40,
    keywords: ['öl', '=oil', 'oil change', '5w', '0w', '10w', 'liqui', 'motul', 'castrol', 'fluid', 'flüssigkeit', 'ölwechsel'] },
  { key: 'engine', label: 'Engine', de: 'Motor', hue: 20,
    keywords: ['engine', 'motor', 'ansaug', 'intake', 'ladeluft', 'intercooler', '=llk', 'chargepipe', 'boost', 'turbo', 'zünd',
      'spark', 'vanos', 'nockenwelle', 'kurbel', 'ventil', 'dichtung', 'drossel', 'luftmassen', '=disa', '=kge', 'injector',
      'einspritz', 'stage', '=tune', 'tuning', '=mhd', 'glüh', 'riemen', 'vakuum', 'unterdruck', 'zylinder'] },
  { key: 'ac', label: 'A/C', de: 'Klima', hue: 190,
    keywords: ['klima', '=a/c', '=ac', 'kältemittel', 'kondensator', 'kompressor', 'air con'] },
  { key: 'cooling', label: 'Cooling', de: 'Kühlung', hue: 185,
    keywords: ['radiator', 'kühler', 'kühl', 'thermostat', 'coolant', 'ausgleichsbehälter', 'expansion', 'wasserpumpe', 'water pump'] },
  { key: 'fuel', label: 'Fuel', de: 'Kraftstoff', hue: 140,
    keywords: ['fuel', '=tank', 'tanken', 'kraftstoff', 'sprit', 'benzin', 'diesel', 'super plus'] },
  { key: 'audio', label: 'Audio', de: 'Audio', hue: 320,
    keywords: ['audio', 'radio', 'speaker', 'lautsprecher', 'subwoofer', 'carplay', 'verstärker', 'amplifier'] },
  { key: 'electrical', label: 'Electrical', de: 'Elektrik', hue: 55,
    keywords: ['electronic', 'elektr', 'sensor', 'steuergerät', '=ecu', 'kabel', 'adapter', '=obd', '=lsz', '=pdc',
      'codier', 'lichtmaschine', 'alternator', 'generator', '=usb', 'dcan'] },
  { key: 'interior', label: 'Interior', de: 'Innenraum', hue: 340,
    keywords: ['interior', 'innen', 'sitz', 'seat', 'teppich', 'dachhimmel', 'schaltknauf', 'shift knob', 'cockpit', 'armaturen'] },
  { key: 'paint', label: 'Paint & detailing', de: 'Lack & Pflege', hue: 270,
    keywords: ['lack', '=paint', 'polish', 'politur', 'aufbereitung', 'detailing', 'keramik', 'ceramic', 'wäsche', 'reinigung', 'cleaning', 'pflege'] },
  { key: 'tools', label: 'Tools', de: 'Werkzeug', hue: 30,
    keywords: ['werkzeug', '=tool', 'tools', '=nuss', 'abzieher', 'drehmoment', 'torque', 'wagenheber', 'jack'] },
  { key: 'labour', label: 'Labour', de: 'Arbeitszeit', hue: 30,
    keywords: ['einbau', 'montage', 'installation', 'labour', 'labor', 'werkstatt', 'arbeitszeit'] },
  { key: 'membership', label: 'Membership', de: 'Mitgliedschaft', hue: 160,
    keywords: ['membership', 'mitglied', '=club', 'adac'] },
]

export const KIND_HUE: Record<EntryKind, number> = { Mod: 25, Maintenance: 210, Expense: 150, Event: 265 }

// Tags that say nothing about the part (TMM groups etc.) → don't let them pick the icon.
const GENERIC_TAGS = new Set(['service', 'repair', 'other', 'maintenance', 'mod', 'mods', 'expense', 'expenses', 'misc',
  'wartung', 'reparatur', 'sonstiges', 'ausgabe', 'ausgaben', 'umbau', 'inspektion'])

const words = (text: string) => text.toLowerCase().split(/[^\p{L}\p{N}&/]+/u).filter(Boolean)

function matches(text: string, keyword: string) {
  const lower = text.toLowerCase()
  if (keyword.startsWith('=')) return words(lower).includes(keyword.slice(1))
  if (keyword.includes(' ') || keyword.length >= 5) return lower.includes(keyword)
  return words(lower).some(w => w.startsWith(keyword))
}

export function categorize(text: string): Category | null {
  if (!text.trim()) return null
  return CATEGORIES.find(c => c.keywords.some(k => matches(text, k))) ?? null
}

/**
 * The title names the actual part ("Osram Nightbreaker LEDs"), tags are often coarse groups ("exterior"),
 * so: title first, then specific tags; null → caller falls back to the entry kind.
 */
export function categoryFor(entry: { title: string; tags: string[] }): Category | null {
  const tagText = entry.tags.filter(t => !GENERIC_TAGS.has(t)).map(t => t.replace(/_/g, ' ')).join(' ')
  return categorize(entry.title) ?? categorize(tagText)
}

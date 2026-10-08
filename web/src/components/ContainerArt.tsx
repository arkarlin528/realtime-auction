/** A small drawn container, coloured by type, instead of stock photos. Reefers get the cooling unit. */
const COLORS: Record<string, string> = {
  '20GP': '#c2410c',
  '40GP': '#1d4ed8',
  '40HC': '#047857',
  '45HC': '#7c3aed',
  '20RF': '#e2e8f0',
  '40RF': '#e2e8f0',
}

export function ContainerArt({ type, size = 'md' }: { type: string; size?: 'md' | 'lg' }) {
  const color = COLORS[type] ?? '#475569'
  const isReefer = type.endsWith('RF')
  const isLong = !type.startsWith('20')
  const width = isLong ? 220 : 140
  const ribs = Math.floor(width / 12)
  const height = size === 'lg' ? 200 : 120

  return (
    <svg viewBox="0 0 260 120" className="container-art" style={{ height }} role="img" aria-label={`${type} container`}>
      <ellipse cx="130" cy="106" rx={width / 2 + 10} ry="6" fill="rgba(15,23,42,.12)" />
      <g transform={`translate(${(260 - width) / 2} 22)`}>
        <rect width={width} height="78" rx="3" fill={color} stroke="rgba(0,0,0,.25)" />
        {Array.from({ length: ribs }, (_, i) => (
          <rect key={i} x={6 + i * 12} y="6" width="5" height="66" rx="1" fill="rgba(0,0,0,.13)" />
        ))}
        <rect x="0" y="0" width={width} height="6" fill="rgba(0,0,0,.18)" />
        <rect x="0" y="72" width={width} height="6" fill="rgba(0,0,0,.22)" />
        {isReefer && (
          <g>
            <rect x={width - 36} y="10" width="30" height="58" rx="2" fill="#94a3b8" />
            <circle cx={width - 21} cy="28" r="9" fill="#475569" />
            <circle cx={width - 21} cy="52" r="9" fill="#475569" />
          </g>
        )}
        <text x="10" y="44" fill={isReefer ? '#0f172a' : 'rgba(255,255,255,.85)'} fontSize="16" fontWeight="700" fontFamily="ui-monospace, monospace">
          {type}
        </text>
      </g>
    </svg>
  )
}

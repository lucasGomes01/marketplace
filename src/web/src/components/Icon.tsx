const paths = {
  search: 'M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14Zm9 16-4.35-4.35',
  cart: 'M3 4h2l2.4 10.2a1.5 1.5 0 0 0 1.46 1.16h8.48a1.5 1.5 0 0 0 1.45-1.12L21 8H6.2M10 20.5h.01M17 20.5h.01',
  user: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm-7 8a7 7 0 0 1 14 0',
  truck: 'M3 7h11v9H3zM14 10h4l3 3v3h-7M7 19.5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3Zm10 0a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3Z',
  shield: 'M12 3 5 6v5c0 4.4 3 8.3 7 10 4-1.7 7-5.6 7-10V6l-7-3Zm-3 9 2 2 4-4',
  store: 'M4 9h16l-1-4H5L4 9Zm0 0v10h16V9M9 19v-5h6v5',
  box: 'M21 8 12 3 3 8v8l9 5 9-5V8ZM3 8l9 5 9-5M12 13v8',
  tag: 'M3 12V4h8l9 9-8 8-9-9Zm5-4h.01',
  package: 'M4 7h16v13H4zM2 4h20v3H2zM10 11h4',
  chevronRight: 'm9 6 6 6-6 6',
  check: 'm5 12 5 5 9-10',
  plus: 'M12 5v14M5 12h14',
  close: 'M6 6l12 12M18 6 6 18',
  filter: 'M4 5h16l-6 8v5l-4 2v-7L4 5Z',
  clock: 'M12 7v5l3 2m6-2a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z',
  image: 'M4 5h16v14H4zM4 15l4-4 4 4 3-3 5 5M15 9h.01',
} as const

export type IconName = keyof typeof paths

/** Ícone de traço (decorativo: o texto ao lado ou o aria-label do botão diz o que é). */
export function Icon({ name, size = 20, className }: { name: IconName; size?: number; className?: string }) {
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <path d={paths[name]} />
    </svg>
  )
}

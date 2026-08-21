const svgUri = (body: string) =>
  `data:image/svg+xml;charset=utf-8,${encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" fill="none" stroke="#fff" stroke-width="4" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`,
  )}`;

export const KIND_ICONS: Record<string, string> = {
  Class: svgUri('<rect x="10" y="14" width="44" height="36" rx="4"/><path d="M10 26h44"/><path d="M18 20h8"/>'),
  Interface: svgUri('<circle cx="32" cy="21" r="9"/><path d="M32 30v16"/><path d="M20 46h24"/>'),
  Struct: svgUri('<rect x="11" y="17" width="19" height="13" rx="2"/><rect x="34" y="17" width="19" height="13" rx="2"/><rect x="23" y="34" width="19" height="13" rx="2"/>'),
  Enum: svgUri('<path d="M26 19h24M26 32h24M26 45h24"/><path d="M15 19h.02M15 32h.02M15 45h.02" stroke-width="6"/>'),
  Record: svgUri('<rect x="12" y="14" width="40" height="36" rx="4"/><path d="M12 25h40"/><path d="M26 25v25"/>'),
  Method: svgUri('<g transform="translate(5.5 5.5) scale(2.2)" stroke-width="1.82"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></g>'),
  Function: svgUri('<path d="M40 12c-7 0-9 4-9 11v18c0 7-2 11-9 11"/><path d="M18 31h22"/>'),
  Module: svgUri('<path d="M32 9l20 11v24L32 55 12 44V20z"/><path d="M12 20l20 11 20-11"/><path d="M32 31v24"/>'),
  Property: svgUri('<path d="M35 11h15v15L27 49 12 34z"/><path d="M41 17h.02" stroke-width="6"/>'),
  Event: svgUri('<path d="M37 8L15 37h13l-5 19 22-29H32z"/>'),
  default: svgUri('<circle cx="32" cy="32" r="13"/>'),
};

export const kindIcon = (kind: string): string => KIND_ICONS[kind] ?? KIND_ICONS.default;

import React from 'react';
import { GLYPHS } from './data.js';
import { clickable } from './common.jsx';

const ICONS = {
  home: <path d="M2.5 7.2 8 2.6l5.5 4.6V13a.6.6 0 0 1-.6.6H3.1a.6.6 0 0 1-.6-.6V7.2Z" strokeLinejoin="round" />,
  buttons: <g><circle cx="5" cy="5" r="2.2" /><circle cx="11" cy="5" r="2.2" /><circle cx="5" cy="11" r="2.2" /><circle cx="11" cy="11" r="2.2" /></g>,
  apps: <g><rect x="2" y="2" width="8" height="8" rx="1.5" /><rect x="6" y="6" width="8" height="8" rx="1.5" /></g>,
  settings: <g><path d="M6.7 1.6h2.6l.3 1.7 1.3.75 1.6-.65 1.3 2.25-1.3 1.1v1.5l1.3 1.1-1.3 2.25-1.6-.65-1.3.75-.3 1.7H6.7l-.3-1.7-1.3-.75-1.6.65-1.3-2.25 1.3-1.1v-1.5l-1.3-1.1 1.3-2.25 1.6.65 1.3-.75.3-1.7Z" /><circle cx="8" cy="8" r="2.1" /></g>
};

const ITEMS = [['home', 'Home'], ['buttons', 'Keymapping'], ['apps', 'App profiles'], ['settings', 'Settings']];
export const PAGE_ORDER = ITEMS.map(([id]) => id);

export default function Sidebar({ page, setPage, kind }) {
  const g = GLYPHS[kind] || GLYPHS.xbox;
  return (
    <nav style={{ width: 244, flex: 'none', background: '#1d1d1d', borderRight: '1px solid rgba(255,255,255,.06)', display: 'flex', flexDirection: 'column', padding: '8px 6px 10px' }}>
      {ITEMS.map(([id, label]) => {
        const on = page === id;
        return (
          <div key={id} {...clickable(() => setPage(id))} className="hov-nav"
            style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 12, height: 38, padding: '0 10px', borderRadius: 5, position: 'relative', background: on ? 'rgba(255,255,255,.08)' : 'transparent' }}>
            <div style={{ position: 'absolute', left: 0, top: 9, bottom: 9, width: 3, borderRadius: 2, background: '#60cdff', opacity: on ? 1 : 0 }} />
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.2" style={{ color: 'rgba(255,255,255,.85)', flex: 'none' }}>{ICONS[id]}</svg>
            <div style={{ fontSize: 14, color: on ? '#fff' : 'rgba(255,255,255,.85)' }}>{label}</div>
          </div>
        );
      })}
      <div style={{ flex: 1 }} />
      <div style={{ padding: '10px 12px', borderTop: '1px solid rgba(255,255,255,.06)', margin: '6px 4px 0', display: 'flex', flexDirection: 'column', gap: 5 }}>
        <div style={{ fontSize: 11, color: 'rgba(255,255,255,.45)', letterSpacing: '.02em' }}>On a controller</div>
        <div style={{ fontSize: 11, color: 'rgba(255,255,255,.62)', lineHeight: 1.5 }}>{g.lb} / {g.rb} switch sections · {g.a} select · {g.b} back</div>
      </div>
    </nav>
  );
}

// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect } from 'react';

export type HearthStatus = 'calm' | 'active' | 'attention' | 'alert' | 'offline' | 'sleeping';

interface ModeCfg {
  flame: string; core: string | null; house: string; halo: string | null;
  haloDur: number; swayDur: number; coreDur: number;
  eyes: string; tongues?: boolean; closedEyes?: boolean; still?: boolean;
}

const CFG: Record<HearthStatus, ModeCfg> = {
  calm:      { flame: '#EDAE49', core: '#FFD36B', house: '#5B7CFF', halo: '#EDAE49', haloDur: 3.6, swayDur: 3.6, coreDur: 3,   eyes: '#5A2600' },
  active:    { flame: '#F7B733', core: '#FFE08A', house: '#5B7CFF', halo: '#FFC24D', haloDur: 1.4, swayDur: 1.3, coreDur: 0.9, eyes: '#5A2600', tongues: true },
  attention: { flame: '#EDAE49', core: '#9FB4FF', house: '#5B7CFF', halo: '#5B7CFF', haloDur: 1.15, swayDur: 2,  coreDur: 1.1, eyes: '#5A2600' },
  alert:     { flame: '#FF7A59', core: '#FFD0B0', house: '#FF6B6B', halo: '#FF6B6B', haloDur: 0.8, swayDur: 0.9, coreDur: 0.6, eyes: '#4A1200' },
  offline:   { flame: '#3a3f4b', core: null,      house: '#4b5261', halo: null,      haloDur: 0,   swayDur: 0,   coreDur: 0,   eyes: '#1b2030', still: true },
  sleeping:  { flame: '#C99640', core: null,      house: '#5B7CFF', halo: '#EDAE49', haloDur: 5.5, swayDur: 5.5, coreDur: 0,   eyes: '#5A2600', closedEyes: true },
};

const KEYFRAMES = `
@keyframes hearthSway{0%,100%{transform:rotate(-2deg)}50%{transform:rotate(2deg)}}
@keyframes hearthCore{0%,100%{opacity:.75;transform:scaleY(.92)}50%{opacity:1;transform:scaleY(1.12)}}
@keyframes hearthHalo{0%,100%{opacity:.3;transform:scale(.85)}50%{opacity:.65;transform:scale(1.1)}}
@keyframes hearthTongue{0%,100%{opacity:0;transform:scale(.55)}35%,65%{opacity:1;transform:scale(1)}}
@media (prefers-reduced-motion:reduce){.hearth-avatar *{animation:none!important}}
`;

function useKeyframes() {
  useEffect(() => {
    if (document.getElementById('hearth-avatar-kf')) return;
    const el = document.createElement('style');
    el.id = 'hearth-avatar-kf';
    el.textContent = KEYFRAMES;
    document.head.appendChild(el);
  }, []);
}

/**
 * Единый аватар/статус домового: дом + дух-огонёк с «ручками».
 * Заменяет пару «квадрат-аватар + уголёк HearthIndicator» в шапке сайдбара.
 * Лицо и ручки видимы от ~28px; ниже — используйте HearthMark (упрощённый знак).
 */
export default function HearthAvatar({ status = 'calm', size = 32, title }: {
  status?: HearthStatus; size?: number; title?: string;
}) {
  useKeyframes();
  const c = CFG[status];
  const sway = c.still ? undefined : `hearthSway ${c.swayDur}s ease-in-out infinite`;
  return (
    <div className="hearth-avatar" title={title} style={{ position: 'relative', width: size, height: size, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}>
      {c.halo && (
        <div style={{ position: 'absolute', width: '82%', height: '82%', borderRadius: '50%', background: `radial-gradient(circle, ${c.halo} 0%, transparent 70%)`, animation: `hearthHalo ${c.haloDur}s ease-in-out infinite` }} />
      )}
      <svg width={size * 0.94} height={size * 0.94} viewBox="0 0 48 48" fill="none" style={{ position: 'relative' }} aria-hidden="true">
        <path d="M24 6 L41 20 V41 a1 1 0 0 1-1 1 H8 a1 1 0 0 1-1-1 V20 Z" fill="none" stroke={c.house} strokeWidth={3} strokeLinejoin="round" />
        <g style={{ animation: sway, transformOrigin: '24px 36px' }}>
          {c.tongues && (<>
            <path d="M29.6 14.2 C31.9 12.3 32.7 16.2 30.6 18.6 C30.9 16.8 30.4 15.5 29.6 14.2 Z" fill={c.flame} style={{ animation: 'hearthTongue 1.5s ease-in-out infinite', transformOrigin: '30px 18px' }} />
            <path d="M17.6 20.3 C15.3 19 15.5 22.4 17.5 24 C17.1 22.6 17.2 21.4 17.6 20.3 Z" fill={c.flame} style={{ animation: 'hearthTongue 1.9s ease-in-out infinite', animationDelay: '-.7s', transformOrigin: '17px 23px' }} />
            <path d="M21.9 9.2 C20.8 7.1 19.2 9.5 20.5 11.7 C20.8 10.6 21.3 9.9 21.9 9.2 Z" fill={c.flame} style={{ animation: 'hearthTongue 1.2s ease-in-out infinite', animationDelay: '-.3s', transformOrigin: '21px 11px' }} />
          </>)}
          <path d="M17 27.5 C13.6 26 13.4 30.8 16.4 33 C15.8 31 16.2 29.2 17 27.5 Z" fill={c.flame} />
          <path d="M31 27.5 C34.4 26 34.6 30.8 31.6 33 C32.2 31 31.8 29.2 31 27.5 Z" fill={c.flame} />
          <path d="M25.5 8 C28.5 14.5 32 19.5 32 27.5 a8 8 0 0 1-16 0 C16 23 17.4 20.2 19.2 16.3 C19.8 18.4 20.4 19 20.9 20 C22.3 16 24.3 12.5 25.5 8 Z" fill={c.flame} />
          {c.core && (
            <path d="M24 13.5 C24.6 18.5 26.6 19.5 26.6 22.5 a2.6 2.6 0 0 1-5.2 0 C21.4 20 23.2 19.5 24 13.5 Z" fill={c.core}
              style={c.coreDur ? { animation: `hearthCore ${c.coreDur}s ease-in-out infinite`, transformOrigin: '24px 20px' } : undefined} />
          )}
          {c.closedEyes ? (<>
            <path d="M20.4 28.6 q1.2 1 2.4 0" stroke={c.eyes} strokeWidth={1.1} fill="none" strokeLinecap="round" />
            <path d="M25.2 28.6 q1.2 1 2.4 0" stroke={c.eyes} strokeWidth={1.1} fill="none" strokeLinecap="round" />
          </>) : (<>
            <circle cx="21.7" cy="28.4" r="1.5" fill={c.eyes} />
            <circle cx="26.3" cy="28.4" r="1.5" fill={c.eyes} />
          </>)}
          <path d="M22 31.8 q2 1.6 4 0" stroke={c.eyes} strokeWidth={1.2} fill="none" strokeLinecap="round" />
        </g>
      </svg>
    </div>
  );
}

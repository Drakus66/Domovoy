// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Maps a device's latest event-log row (batch provenance, Epic 2G) to a compact "who changed it last"
// chip for the tile: Вы / ML / Датчик / Правило · {имя} / Блок · {имя} / Присутствие. The concrete
// rule/block name is resolved best-effort via an optional nameOf (the grid already loads rules); without
// it the chip shows the generic category label.

import i18n from 'i18next';
import { LatestEvent } from '../../api/history';

export type ProvenanceAuthor = 'user' | 'rule' | 'block' | 'ml' | 'device' | 'presence';

export interface Provenance {
  author: ProvenanceAuthor;
  /** Short chip label, e.g. "Вы", "ML", "Правило · Вечер". */
  label: string;
  /** True when automation (rule/ml/block) did it — the chip is accented to stand out from manual/sensor. */
  accented: boolean;
  /** ISO timestamp of the last change (for the relative-time caption). */
  when: string;
}

const AUTHORS: Record<string, ProvenanceAuthor> = {
  user: 'user', rule: 'rule', block: 'block', ml: 'ml', device: 'device', presence: 'presence',
};

/** Resolve a trigger id (rule/block) to a display name; returns null when unknown. */
export type NameOf = (triggerId?: string | null) => string | null;

export function describeProvenance(ev: LatestEvent, nameOf?: NameOf): Provenance {
  const author = AUTHORS[ev.triggerSource] ?? 'device';
  const t = (k: string, o?: Record<string, unknown>) => i18n.t(`devices:provenance.${k}`, o ?? {});
  const name = nameOf?.(ev.triggerId ?? ev.ruleId);

  let label: string;
  switch (author) {
    case 'user': label = t('user'); break;
    case 'ml': label = t('ml'); break;
    case 'presence': label = t('presence'); break;
    case 'rule': label = name ? t('ruleNamed', { name }) : t('rule'); break;
    case 'block': label = name ? t('blockNamed', { name }) : t('block'); break;
    case 'device':
    default: label = t('device'); break;
  }

  return { author, label, when: ev.timestamp, accented: author === 'rule' || author === 'ml' || author === 'block' };
}

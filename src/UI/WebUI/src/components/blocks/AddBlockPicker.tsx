// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Dialog, DialogTitle, DialogContent, TextField, InputAdornment, List, ListSubheader,
  ListItemButton, ListItemText, Typography, Box, Card, CardActionArea, Chip, Stack,
} from '@mui/material';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import LoginRoundedIcon from '@mui/icons-material/LoginRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import { BlockCatalogEntry } from '../../api/blocks';
import { blockTypeDesc, blockTypeTitle, paramLabel } from '../../i18n/blockLabels';

/**
 * Categorized block-type picker (roadmap Epic 2Q). Replaces the flat "one chip per type" wall that stopped
 * scaling past ~30 types: a search field plus the catalog grouped by the server-supplied `category`
 * (templates first — the non-expert path — then control/filter/logic/time/math/ml). Picking a type hands the
 * entry to the existing typed authoring dialog.
 *
 * The <b>template</b> group renders as a richer gallery (roadmap Epic 2Q template-gallery tail): each ready-made
 * recipe shows what it tunes (its exposed passthrough params), what to bind (inputs) and what it drives
 * (outputs) — the non-expert can judge a template before creating it. Other groups stay a compact list.
 */

// Display order of the category groups; unknown categories fall to the end ("other").
const CATEGORY_ORDER = ['template', 'control', 'filter', 'logic', 'time', 'math', 'ml', 'other'];

/** A catalog entry paired with its localized display strings (the entry itself stays untouched for onPick). */
type LocalizedEntry = { entry: BlockCatalogEntry; title: string; description: string };

export default function AddBlockPicker({
  open, catalog, onPick, onClose,
}: {
  open: boolean;
  catalog: BlockCatalogEntry[];
  onPick: (entry: BlockCatalogEntry) => void;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('blocks');
  const [query, setQuery] = useState('');

  const groups = useMemo(() => {
    // Overlay the client-side translation on the server catalog (display only — onPick hands back the
    // original entry); search matches both the localized text and the original English title/typeId.
    const localized = catalog.map((e) => ({
      entry: e,
      title: blockTypeTitle(e.typeId, e.title),
      description: blockTypeDesc(e.typeId, e.description),
    }));

    const q = query.trim().toLowerCase();
    const matches = q.length === 0
      ? localized
      : localized.filter(({ entry, title, description }) =>
        title.toLowerCase().includes(q) || description.toLowerCase().includes(q)
        || entry.typeId.toLowerCase().includes(q)
        || entry.title.toLowerCase().includes(q) || entry.description.toLowerCase().includes(q));

    const byCat = new Map<string, LocalizedEntry[]>();
    for (const e of matches) {
      const cat = CATEGORY_ORDER.includes(e.entry.category ?? '') ? e.entry.category! : 'other';
      const list = byCat.get(cat) ?? [];
      list.push(e);
      byCat.set(cat, list);
    }
    return CATEGORY_ORDER
      .filter((c) => byCat.has(c))
      .map((c) => ({ category: c, entries: byCat.get(c)!.sort((a, b) => a.title.localeCompare(b.title)) }));
    // i18n.language: blockTypeTitle/Desc read the global i18n, so re-localize when the language switches.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [catalog, query, i18n.language]);

  const close = () => { setQuery(''); onClose(); };

  return (
    <Dialog open={open} onClose={close} fullWidth maxWidth="sm"
      PaperProps={{ sx: { height: '70vh' } }}>
      <DialogTitle sx={{ pb: 1 }}>{t('picker.title')}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', pt: '4px !important' }}>
        <TextField
          size="small" fullWidth autoFocus value={query} placeholder={t('picker.search')}
          onChange={(e) => setQuery(e.target.value)}
          InputProps={{
            startAdornment: <InputAdornment position="start"><SearchRoundedIcon fontSize="small" /></InputAdornment>,
          }}
          sx={{ mb: 1 }}
        />
        <Box sx={{ flex: 1, overflowY: 'auto' }}>
          {groups.length === 0 && (
            <Typography color="text.secondary" textAlign="center" py={4}>{t('picker.noMatch')}</Typography>
          )}
          {groups.map((g) => (
            g.category === 'template' ? (
              <Box key={g.category} sx={{ mb: 1 }}>
                <ListSubheader disableSticky sx={{ lineHeight: '32px', bgcolor: 'transparent', px: 0 }}>
                  {t(`category.${g.category}`)}
                </ListSubheader>
                <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 1 }}>
                  {g.entries.map((e) => (
                    <TemplateCard key={e.entry.typeId} item={e} onPick={() => { onPick(e.entry); setQuery(''); }} />
                  ))}
                </Box>
              </Box>
            ) : (
              <List key={g.category} dense disablePadding
                subheader={
                  <ListSubheader disableSticky sx={{ lineHeight: '32px', bgcolor: 'transparent' }}>
                    {t(`category.${g.category}`)}
                  </ListSubheader>
                }>
                {g.entries.map((e) => (
                  <ListItemButton key={e.entry.typeId} onClick={() => { onPick(e.entry); setQuery(''); }} sx={{ borderRadius: 1.5 }}>
                    <ListItemText
                      primary={e.title}
                      secondary={e.description}
                      primaryTypographyProps={{ fontWeight: 600, fontSize: 14 }}
                      secondaryTypographyProps={{ fontSize: 12 }}
                    />
                  </ListItemButton>
                ))}
              </List>
            )
          ))}
        </Box>
      </DialogContent>
    </Dialog>
  );
}

/**
 * A gallery card for a ready-made template (roadmap Epic 2Q). Surfaces the three things a non-expert needs to
 * judge a recipe before creating it: what it tunes (exposed passthrough params + their defaults), what to bind
 * (inputs) and what it drives (outputs). Clicking hands the entry to the typed authoring dialog.
 */
function TemplateCard({ item, onPick }: { item: LocalizedEntry; onPick: () => void }) {
  const { t } = useTranslation('blocks');
  const { entry } = item;
  return (
    <Card variant="outlined" sx={{ borderRadius: 2, height: '100%' }}>
      <CardActionArea onClick={onPick} sx={{ height: '100%', p: 1.5, alignItems: 'flex-start' }}>
        <Typography fontWeight={600} fontSize={14}>{item.title}</Typography>
        <Typography variant="body2" color="text.secondary" fontSize={12} sx={{ mt: 0.25 }}>
          {item.description}
        </Typography>
        <Stack spacing={0.75} sx={{ mt: 1 }}>
          {entry.params.length > 0 && (
            <ChipRow icon={<TuneRoundedIcon sx={{ fontSize: 14 }} />} label={t('gallery.tunable')}
              items={entry.params.map((p) => `${paramLabel(entry.typeId, p)} · ${p.default}`)} />
          )}
          {entry.inputs.length > 0 && (
            <ChipRow icon={<LoginRoundedIcon sx={{ fontSize: 14 }} />} label={t('gallery.needs')}
              items={entry.inputs.map((i) => i.name)} />
          )}
          {entry.outputs.length > 0 && (
            <ChipRow icon={<BoltRoundedIcon sx={{ fontSize: 14 }} />} label={t('gallery.drives')}
              items={entry.outputs.map((o) => o.id)} />
          )}
        </Stack>
      </CardActionArea>
    </Card>
  );
}

/** One labelled row of small chips inside a template card (tunable params / inputs / outputs). */
function ChipRow({ icon, label, items }: { icon: React.ReactNode; label: string; items: string[] }) {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, flexWrap: 'wrap' }}>
      <Box sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.25, color: 'text.secondary' }}>
        {icon}
        <Typography variant="caption" color="text.secondary">{label}</Typography>
      </Box>
      {items.map((it) => (
        <Chip key={it} label={it} size="small" variant="outlined" sx={{ height: 20, fontSize: 11 }} />
      ))}
    </Box>
  );
}

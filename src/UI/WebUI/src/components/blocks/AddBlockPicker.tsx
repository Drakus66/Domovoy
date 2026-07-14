// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Dialog, DialogTitle, DialogContent, TextField, InputAdornment, List, ListSubheader,
  ListItemButton, ListItemText, Typography, Box,
} from '@mui/material';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import { BlockCatalogEntry } from '../../api/blocks';

/**
 * Categorized block-type picker (roadmap Epic 2Q). Replaces the flat "one chip per type" wall that stopped
 * scaling past ~30 types: a search field plus the catalog grouped by the server-supplied `category`
 * (templates first — the non-expert path — then control/filter/logic/time/math/ml). Picking a type hands the
 * entry to the existing typed authoring dialog.
 */

// Display order of the category groups; unknown categories fall to the end ("other").
const CATEGORY_ORDER = ['template', 'control', 'filter', 'logic', 'time', 'math', 'ml', 'other'];

export default function AddBlockPicker({
  open, catalog, onPick, onClose,
}: {
  open: boolean;
  catalog: BlockCatalogEntry[];
  onPick: (entry: BlockCatalogEntry) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation('blocks');
  const [query, setQuery] = useState('');

  const groups = useMemo(() => {
    const q = query.trim().toLowerCase();
    const matches = q.length === 0
      ? catalog
      : catalog.filter((e) =>
        e.title.toLowerCase().includes(q) || e.typeId.toLowerCase().includes(q) || e.description.toLowerCase().includes(q));

    const byCat = new Map<string, BlockCatalogEntry[]>();
    for (const e of matches) {
      const cat = CATEGORY_ORDER.includes(e.category ?? '') ? e.category! : 'other';
      const list = byCat.get(cat) ?? [];
      list.push(e);
      byCat.set(cat, list);
    }
    return CATEGORY_ORDER
      .filter((c) => byCat.has(c))
      .map((c) => ({ category: c, entries: byCat.get(c)!.sort((a, b) => a.title.localeCompare(b.title)) }));
  }, [catalog, query]);

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
            <List key={g.category} dense disablePadding
              subheader={
                <ListSubheader disableSticky sx={{ lineHeight: '32px', bgcolor: 'transparent' }}>
                  {t(`category.${g.category}`)}
                </ListSubheader>
              }>
              {g.entries.map((e) => (
                <ListItemButton key={e.typeId} onClick={() => { onPick(e); setQuery(''); }} sx={{ borderRadius: 1.5 }}>
                  <ListItemText
                    primary={e.title}
                    secondary={e.description}
                    primaryTypographyProps={{ fontWeight: 600, fontSize: 14 }}
                    secondaryTypographyProps={{ fontSize: 12 }}
                  />
                </ListItemButton>
              ))}
            </List>
          ))}
        </Box>
      </DialogContent>
    </Dialog>
  );
}

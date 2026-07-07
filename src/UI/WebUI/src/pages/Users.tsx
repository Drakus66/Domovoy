import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import {
  Container, Box, Typography, Stack, Button, IconButton, LinearProgress, Alert,
  Card, CardContent, Tooltip, Dialog, DialogTitle, DialogContent, DialogActions,
  TextField, Chip, Tabs, Tab, FormControlLabel, Checkbox, Switch, Divider,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import PersonRoundedIcon from '@mui/icons-material/PersonRounded';
import BadgeRoundedIcon from '@mui/icons-material/BadgeRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import { securityApi, Role, RoleInput, User, UserInput } from '../api/security';

const EMPTY_ROLE: RoleInput = { name: '', description: '', permissions: [] };
const EMPTY_USER: UserInput = { displayName: '', email: '', roleIds: [], enabled: true };

export default function Users() {
  const { t } = useTranslation('users');
  const [tab, setTab] = useState(0);

  // Permission ids carry dots (e.g. "devices.view"); i18next uses '.' as a key separator, so map to a
  // dot-free subkey for the label lookup and fall back to the raw id if a translation is missing.
  const permLabel = (perm: string) => t(`perm.${perm.replace(/\./g, '_')}`, { defaultValue: perm });

  // Built-in role name/description are seeded in English in the DB; translate them by stable role id and
  // fall back to the stored value for operator-created roles.
  const roleLabel = (r: Role) =>
    r.isBuiltIn ? t(`builtInRole.${r.id}.name`, { defaultValue: r.name }) : r.name;
  const roleDesc = (r: Role) =>
    r.isBuiltIn ? t(`builtInRole.${r.id}.description`, { defaultValue: r.description ?? '' }) : r.description;

  const [users, setUsers] = useState<User[]>([]);
  const [roles, setRoles] = useState<Role[]>([]);
  const [permissions, setPermissions] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [userDraft, setUserDraft] = useState<UserInput | null>(null);
  const [editingUser, setEditingUser] = useState<User | null>(null);
  const [roleDraft, setRoleDraft] = useState<RoleInput | null>(null);
  const [editingRole, setEditingRole] = useState<Role | null>(null);

  const fetchAll = useCallback(async () => {
    setError(null);
    try {
      const [u, r, p] = await Promise.all([
        securityApi.getUsers(),
        securityApi.getRoles(),
        securityApi.getPermissions(),
      ]);
      setUsers(u);
      setRoles(r);
      setPermissions(p);
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchAll(); }, [fetchAll]);

  const roleName = useMemo(() => new Map(roles.map((r) => [r.id, roleLabel(r)])), [roles]);

  // --- user dialog ---
  const openCreateUser = () => { setEditingUser(null); setUserDraft({ ...EMPTY_USER }); };
  const openEditUser = (u: User) => {
    setEditingUser(u);
    setUserDraft({ displayName: u.displayName, email: u.email ?? '', roleIds: [...u.roleIds], enabled: u.enabled });
  };
  const closeUser = () => { setUserDraft(null); setEditingUser(null); };
  const saveUser = async () => {
    if (!userDraft || !userDraft.displayName.trim()) return;
    try {
      if (editingUser) await securityApi.updateUser(editingUser.id, userDraft);
      else await securityApi.createUser(userDraft);
      closeUser();
      await fetchAll();
    } catch { setError(t('errors.save')); }
  };
  const removeUser = async (u: User) => {
    if (!window.confirm(i18n.t('users:deleteUserConfirm', { name: u.displayName }))) return;
    try { await securityApi.deleteUser(u.id); await fetchAll(); } catch { setError(t('errors.delete')); }
  };
  const toggleUserRole = (id: string) => {
    if (!userDraft) return;
    const has = userDraft.roleIds.includes(id);
    setUserDraft({ ...userDraft, roleIds: has ? userDraft.roleIds.filter((x) => x !== id) : [...userDraft.roleIds, id] });
  };

  // --- role dialog ---
  const openCreateRole = () => { setEditingRole(null); setRoleDraft({ ...EMPTY_ROLE }); };
  const openEditRole = (r: Role) => {
    setEditingRole(r);
    setRoleDraft({ name: r.name, description: r.description ?? '', permissions: [...r.permissions] });
  };
  const closeRole = () => { setRoleDraft(null); setEditingRole(null); };
  const saveRole = async () => {
    if (!roleDraft || !roleDraft.name.trim()) return;
    try {
      if (editingRole) await securityApi.updateRole(editingRole.id, roleDraft);
      else await securityApi.createRole(roleDraft);
      closeRole();
      await fetchAll();
    } catch { setError(t('errors.save')); }
  };
  const removeRole = async (r: Role) => {
    if (!window.confirm(i18n.t('users:deleteRoleConfirm', { name: roleLabel(r) }))) return;
    try { await securityApi.deleteRole(r.id); await fetchAll(); } catch { setError(t('errors.deleteRole')); }
  };
  const toggleRolePerm = (perm: string) => {
    if (!roleDraft) return;
    const has = roleDraft.permissions.includes(perm);
    setRoleDraft({ ...roleDraft, permissions: has ? roleDraft.permissions.filter((x) => x !== perm) : [...roleDraft.permissions, perm] });
  };

  const sortedUsers = useMemo(
    () => [...users].sort((a, b) => a.displayName.localeCompare(b.displayName)),
    [users],
  );
  const sortedRoles = useMemo(
    () => [...roles].sort((a, b) => Number(b.isBuiltIn) - Number(a.isBuiltIn) || roleLabel(a).localeCompare(roleLabel(b))),
    [roles],
  );

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Box mb={2}>
          <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
          <Typography variant="caption" color="text.secondary">{t('subtitle')}</Typography>
        </Box>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 3 }}>
          <Tab label={t('tabs.users')} />
          <Tab label={t('tabs.roles')} />
        </Tabs>

        {tab === 0 && (
          <>
            <Stack direction="row" justifyContent="flex-end" mb={2}>
              <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={openCreateUser}>{t('newUser')}</Button>
            </Stack>
            {sortedUsers.length === 0 && !loading ? (
              <Box textAlign="center" py={8}>
                <PersonRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
                <Typography color="text.secondary">{t('emptyUsers')}</Typography>
              </Box>
            ) : (
              <Stack spacing={1.5}>
                {sortedUsers.map((u) => (
                  <Card key={u.id} variant="outlined">
                    <CardContent sx={{ display: 'flex', alignItems: 'center', gap: 2, py: 1.5, '&:last-child': { pb: 1.5 } }}>
                      <PersonRoundedIcon color={u.enabled ? 'primary' : 'disabled'} />
                      <Box flex={1} minWidth={0}>
                        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                          <Typography fontWeight={700}>{u.displayName}</Typography>
                          {!u.enabled && <Chip size="small" label={t('disabled')} color="default" variant="outlined" />}
                          {u.roleIds.map((rid) => (
                            <Chip key={rid} size="small" label={roleName.get(rid) ?? rid} variant="outlined" />
                          ))}
                          {u.roleIds.length === 0 && (
                            <Typography variant="caption" color="text.secondary">{t('noRoles')}</Typography>
                          )}
                        </Stack>
                        {u.email && <Typography variant="body2" color="text.secondary" noWrap>{u.email}</Typography>}
                      </Box>
                      <Tooltip title={t('actions.edit')}><IconButton onClick={() => openEditUser(u)}><EditRoundedIcon /></IconButton></Tooltip>
                      <Tooltip title={t('actions.delete')}><IconButton onClick={() => removeUser(u)}><DeleteOutlineRoundedIcon /></IconButton></Tooltip>
                    </CardContent>
                  </Card>
                ))}
              </Stack>
            )}
          </>
        )}

        {tab === 1 && (
          <>
            <Stack direction="row" justifyContent="flex-end" mb={2}>
              <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={openCreateRole}>{t('newRole')}</Button>
            </Stack>
            <Stack spacing={1.5}>
              {sortedRoles.map((r) => (
                <Card key={r.id} variant="outlined">
                  <CardContent sx={{ display: 'flex', alignItems: 'flex-start', gap: 2, py: 1.5, '&:last-child': { pb: 1.5 } }}>
                    <BadgeRoundedIcon color="primary" sx={{ mt: 0.25 }} />
                    <Box flex={1} minWidth={0}>
                      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                        <Typography fontWeight={700}>{roleLabel(r)}</Typography>
                        {r.isBuiltIn && (
                          <Chip size="small" icon={<LockRoundedIcon sx={{ fontSize: 14 }} />} label={t('builtIn')} variant="outlined" />
                        )}
                      </Stack>
                      {roleDesc(r) && <Typography variant="body2" color="text.secondary">{roleDesc(r)}</Typography>}
                      <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap mt={0.75}>
                        {r.permissions.map((p) => (
                          <Chip key={p} size="small" label={permLabel(p)} variant="filled" sx={{ bgcolor: 'action.hover' }} />
                        ))}
                        {r.permissions.length === 0 && (
                          <Typography variant="caption" color="text.secondary">{t('noPermissions')}</Typography>
                        )}
                      </Stack>
                    </Box>
                    <Tooltip title={t('actions.edit')}><IconButton onClick={() => openEditRole(r)}><EditRoundedIcon /></IconButton></Tooltip>
                    <Tooltip title={r.isBuiltIn ? t('builtInLocked') : t('actions.delete')}>
                      <span>
                        <IconButton onClick={() => removeRole(r)} disabled={r.isBuiltIn}><DeleteOutlineRoundedIcon /></IconButton>
                      </span>
                    </Tooltip>
                  </CardContent>
                </Card>
              ))}
            </Stack>
          </>
        )}
      </Box>

      {/* User dialog */}
      <Dialog open={userDraft !== null} onClose={closeUser} fullWidth maxWidth="sm">
        <DialogTitle>{editingUser ? t('userDialog.editTitle') : t('userDialog.newTitle')}</DialogTitle>
        <DialogContent>
          {userDraft && (
            <Stack spacing={2} mt={1}>
              <TextField
                label={t('userDialog.displayName')} value={userDraft.displayName} autoFocus required fullWidth
                onChange={(e) => setUserDraft({ ...userDraft, displayName: e.target.value })}
              />
              <TextField
                label={t('userDialog.email')} value={userDraft.email ?? ''} fullWidth
                onChange={(e) => setUserDraft({ ...userDraft, email: e.target.value })}
              />
              <Box>
                <Typography variant="subtitle2" gutterBottom>{t('userDialog.roles')}</Typography>
                {roles.length === 0 ? (
                  <Typography variant="body2" color="text.secondary">{t('userDialog.noRolesYet')}</Typography>
                ) : (
                  <Stack>
                    {sortedRoles.map((r) => (
                      <FormControlLabel
                        key={r.id}
                        control={<Checkbox checked={userDraft.roleIds.includes(r.id)} onChange={() => toggleUserRole(r.id)} />}
                        label={roleLabel(r)}
                      />
                    ))}
                  </Stack>
                )}
              </Box>
              <FormControlLabel
                control={<Switch checked={userDraft.enabled} onChange={(e) => setUserDraft({ ...userDraft, enabled: e.target.checked })} />}
                label={t('userDialog.enabled')}
              />
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={closeUser}>{t('actions.cancel')}</Button>
          <Button variant="contained" onClick={saveUser} disabled={!userDraft?.displayName.trim()}>{t('actions.save')}</Button>
        </DialogActions>
      </Dialog>

      {/* Role dialog */}
      <Dialog open={roleDraft !== null} onClose={closeRole} fullWidth maxWidth="sm">
        <DialogTitle>{editingRole ? t('roleDialog.editTitle') : t('roleDialog.newTitle')}</DialogTitle>
        <DialogContent>
          {roleDraft && (
            <Stack spacing={2} mt={1}>
              <TextField
                label={t('roleDialog.name')} value={roleDraft.name} autoFocus required fullWidth
                disabled={editingRole?.isBuiltIn}
                helperText={editingRole?.isBuiltIn ? t('roleDialog.builtInNameLocked') : undefined}
                onChange={(e) => setRoleDraft({ ...roleDraft, name: e.target.value })}
              />
              <TextField
                label={t('roleDialog.description')} value={roleDraft.description ?? ''} fullWidth
                onChange={(e) => setRoleDraft({ ...roleDraft, description: e.target.value })}
              />
              <Divider />
              <Typography variant="subtitle2">{t('roleDialog.permissions')}</Typography>
              <Stack>
                {permissions.map((p) => (
                  <FormControlLabel
                    key={p}
                    control={<Checkbox checked={roleDraft.permissions.includes(p)} onChange={() => toggleRolePerm(p)} />}
                    label={
                      <Box>
                        <Typography variant="body2">{permLabel(p)}</Typography>
                        <Typography variant="caption" color="text.secondary">{p}</Typography>
                      </Box>
                    }
                  />
                ))}
              </Stack>
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={closeRole}>{t('actions.cancel')}</Button>
          <Button variant="contained" onClick={saveRole} disabled={!roleDraft?.name.trim()}>{t('actions.save')}</Button>
        </DialogActions>
      </Dialog>
    </Container>
  );
}

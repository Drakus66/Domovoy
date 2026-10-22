# Domovoy — логотип / иконка (финал: «с ручками»)

Знак: дом-силуэт (кров, #5B7CFF) + дух-огонёк (#EDAE49) со светящимся ядром (#FFD36B),
глазами и ртом жжёно-коричневого (#5A2600) и «ручками» — боковыми язычками на уровне рта.
Идеология: домовой — тихий хранитель у очага; значок несёт и личность (аватар), и статус (свечение).

## Файлы

| Файл | Назначение |
|---|---|
| `domovoy-mark.svg` | Основной знак (тёмный фон), с лицом и ручками. От ~28px |
| `domovoy-mark-light.svg` | Для светлого фона (#3355DD / #E0932B) |
| `domovoy-mark-mono.svg` | Монохром, наследует `currentColor`; лицо намеренно опущено |
| `favicon.svg` | Упрощённый знак для 16–24px: без лица и ручек, толще штрих |
| `app-icon.svg` | 512×512 плитка (скругление 120, градиент #1b2030→#12141a) |
| `HearthAvatar.tsx` | React-компонент: аватар + статус в одном, анимированный |

## HearthAvatar

```tsx
import HearthAvatar from './HearthAvatar';
<HearthAvatar status="calm" size={34} title="Домовой: спокоен" />
```

Проп `status`:
- `calm` — медленное дыхание ядра и ореола (по умолчанию)
- `active` — быстрее + 3 язычка вспыхивают/гаснут
- `attention` — синий пульс ореола + синеватое ядро (ждёт решения — есть proposals)
- `alert` — красный, частый пульс
- `offline` — серый, без анимации
- `sleeping` — тусклый, глаза-щёлки, редкое свечение (ночной режим)

Уважает `prefers-reduced-motion`. Keyframes инжектятся один раз (`#hearth-avatar-kf`).

## Размещение в репозитории (внедрено)

- `HearthAvatar.tsx` → `src/UI/WebUI/src/components/common/HearthAvatar.tsx`; в шапке сайдбара
  его рендерит `components/layout/BrandHearth.tsx` — единый аватар+статус вместо пары
  «градиентный квадрат + уголёк HearthIndicator», ссылка на `/status` сохранена.
- Маппинг статуса (в `BrandHearth`): метрики недоступны/0 служб → `offline`; часть служб
  лежит → `alert`; pending proposals > 0 → `attention`; автоматизация действовала за
  последнюю минуту → `active`; режим дома Night → `sleeping`; иначе → `calm`.
- Для мест меньше 28px — `src/UI/WebUI/src/components/common/HearthMark.tsx`: статичный
  упрощённый знак (геометрия favicon), цвета из активной темы (дом = primary,
  пламя = secondary). Заменил «угольки» у дайджеста (HomeStateBand, 18px) и в панели
  «Домовой сегодня» (DomovoyRail, 16px).
- `favicon.svg` → `src/UI/WebUI/public/favicon.svg` (вкладка браузера, `index.html`).
- `app-icon.svg` → `src/UI/WebUI/public/domovoy-icon.svg` (apple-touch + PWA `any`);
  полнокадровый вариант без скругления `domovoy-icon-maskable.svg` — для PWA `maskable`.

## Гайдлайны
- Меньше 28px — только `favicon.svg` (лицо и ручки замыливаются).
- Не менять пропорции пламени и позицию лица; ореол всегда radial-gradient → transparent.
- Цвета брать из темы `domovoy`: primary #5B7CFF, secondary/ember #EDAE49.

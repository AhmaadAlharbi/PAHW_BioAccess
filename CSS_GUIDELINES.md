# CSS Guidelines

## 1) الهدف

المشروع يستخدم طبقتين CSS:

- `FingerprintManagementSystem.Web/wwwroot/css/app.css` كنظام CSS منظم (طبقة قابلة لإعادة الاستخدام).
- `FingerprintManagementSystem.Web/wwwroot/css/site.css` كملف legacy/base (لا نضيف عليه CSS جديد).

---

## 2) القواعد الأساسية

- ❌ ممنوع استخدام:
  - `style=""`
  - `<style>...</style>` داخل الـ Views (`.cshtml`)

- ✅ يجب استخدام:
  - classes من `app.css` (Utilities/Components/Page-scoped)

---

## 3) Naming Convention

- `u-` = Utilities (مثل spacing, flex, alignment…)
  - مثال: `u-flex`, `u-gap-10`, `u-mb-12`
- `c-` = Components (مثل cards, sections, action bars…)
  - مثال: `c-actionbar`, `c-help-text`
- Page-specific classes = فقط عند الحاجة لتفادي تعارض CSS
  - الأفضل إضافة page root wrapper ثم scoping تحته (مثال: `.employees-search-page ...`)

---

## 4) أين نضيف CSS الجديد؟

- كل CSS جديد يجب أن يضاف إلى:
  - `FingerprintManagementSystem.Web/wwwroot/css/app.css`

- لا نضيف CSS إلى:
  - `FingerprintManagementSystem.Web/wwwroot/css/site.css`

---

## 5) متى نستخدم inline style؟

- فقط في حالات نادرة جدًا (مثلاً values ديناميكية لا يمكن تمثيلها بكلاسات).
- ويجب أن يكون مبررًا ومحدودًا قدر الإمكان.

---

## 6) مثال بسيط

قبل:

```html
<div style="display:flex; gap:10px;">
```

بعد:

```html
<div class="u-flex u-gap-10">
```

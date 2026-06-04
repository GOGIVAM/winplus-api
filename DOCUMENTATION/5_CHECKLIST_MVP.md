# ✅ CHECKLIST MVP - TÂCHES À COMPLÉTER

**Date** : 6 décembre 2025  
**Deadline** : 27 décembre 2025  
**Statut** : À commencer (7 décembre)

---

## 🎯 RÉCAPITULATIF RAPIDE

| Catégorie | Tâches | Complétées | % |
|-----------|--------|-----------|---|
| Services | 8 | 0 | 0% |
| Pages | 12 | 0 | 0% |
| Components | 15 | 0 | 0% |
| Tests | 5 | 0 | 0% |
| **TOTAL** | **40** | **0** | **0%** |

---

## 🔧 SERVICES (8 tâches)

### Priorité CRITIQUE

#### ⬜ 1. cartService.ts
```typescript
Localisation : frontend/src/services/cartService.ts
Dépendances : contexts/CartContext.tsx, types/catalog.ts
Status : ⬜ À faire
Timeline : Lun 7 déc (3h)

✓ Fonctions requises:
  - addToCart(subject: Subject): void
  - removeFromCart(id: string): void
  - updateQuantity(id: string, qty: number): void
  - clearCart(): void
  - getCartTotal(): number
  - applyPromoCode(code: string): void
  - getCart(): CartItem[]

✓ Tests:
  - unit tests pour chaque fonction
  - integration tests avec CartContext
  - edge cases handling
```

#### ⬜ 2. paymentService.ts
```typescript
Localisation : frontend/src/services/paymentService.ts
Dépendances : axios, types/index.ts
Status : ⬜ À faire
Timeline : Lun 7 déc (4h)

✓ Fonctions requises:
  - initializePayment(cartData: Cart): Promise<PaymentIntent>
  - validatePaymentMethod(method: PaymentMethod): boolean
  - processPayment(token: string, amount: number): Promise<Receipt>
  - getPaymentStatus(paymentId: string): Promise<PaymentStatus>
  - retryPayment(paymentId: string): Promise<Receipt>
  - generateReceipt(order: Order): string

✓ Intégrations:
  - Stripe API
  - Backend payment endpoint
  - Error handling & logging
```

#### ⬜ 3. favoriteService.ts
```typescript
Localisation : frontend/src/services/favoriteService.ts
Dépendances : apiClient, localStorage
Status : ⬜ À faire
Timeline : Mar 8 déc (2h)

✓ Fonctions requises:
  - addFavorite(subjectId: string): Promise<void>
  - removeFavorite(subjectId: string): Promise<void>
  - getFavorites(): Promise<Subject[]>
  - isFavorite(subjectId: string): boolean
  - syncFavorites(userId: string): Promise<void>
```

### Priorité IMPORTANTE

#### ⬜ 4. historyService.ts
```typescript
Localisation : frontend/src/services/historyService.ts
Status : ⬜ À faire
Timeline : Mer 16 déc (2h)

✓ Fonctions requises:
  - getOrderHistory(userId: string): Promise<Order[]>
  - getOrderDetails(orderId: string): Promise<OrderDetails>
  - trackOrder(orderId: string): Promise<TrackingInfo>
  - downloadReceipt(orderId: string): Promise<Blob>
  - cancelOrder(orderId: string): Promise<boolean>
```

#### ⬜ 5. aiService.ts
```typescript
Localisation : frontend/src/services/aiService.ts
Status : ⬜ À faire
Timeline : Jeu 19 déc (3h)

✓ Fonctions requises:
  - getRecommendations(userId: string): Promise<Subject[]>
  - generateStudyPlan(subjectId: string): Promise<StudyPlan>
  - getPredictions(userId: string): Promise<Predictions>
  - analyzeLearningStyle(userId: string): Promise<LearningStyle>
```

### Priorité OPTIONNELLE (Post-MVP)

#### ⬜ 6. notificationService.ts
#### ⬜ 7. analyticsService.ts
#### ⬜ 8. downloadService.ts

---

## 📄 PAGES (12 tâches)

### Priorité CRITIQUE

#### ⬜ P1. SubjectDetailsPage.tsx
```
Localisation : frontend/src/pages/SubjectDetailsPage.tsx
État actuel : 60%
Cible : 100%
Timeline : Lun 14 - Mar 15 déc (6h)
Dépendances : SubjectDetailView.tsx, cartService.ts

✓ Sections à implémenter:
  [ ] Hero section (image + titre)
  [ ] Course metadata (durée, niveau, instructeur)
  [ ] Description complète
  [ ] Curriculum outline
  [ ] Reviews & ratings section
  [ ] "Add to Cart" button
  [ ] Related courses suggestion
  [ ] Student statistics
  [ ] CTA section (signup/login)

✓ Interactions:
  [ ] Add to cart workflow
  [ ] Favorite toggle
  [ ] Share functionality
  [ ] Review submission (auth required)
```

#### ⬜ P2. CartPage.tsx
```
Localisation : frontend/src/pages/CartPage.tsx
État actuel : 30%
Cible : 100%
Timeline : Mar 15 - Mer 16 déc (6h)
Dépendances : CartItem.tsx, cartService.ts, paymentService.ts

✓ Sections à implémenter:
  [ ] Cart items list
  [ ] Item quantity controls
  [ ] Remove item buttons
  [ ] Promo code input
  [ ] Cart summary (subtotal, tax, total)
  [ ] Checkout button
  [ ] Continue shopping link
  [ ] Bundle suggestions
  [ ] Empty state (if no items)
  [ ] Loading state

✓ Interactions:
  [ ] Update quantities
  [ ] Remove items
  [ ] Apply promo codes
  [ ] Proceed to checkout
  [ ] Cart persistence (localStorage)
```

#### ⬜ P3. DashboardPage.tsx
```
Localisation : frontend/src/pages/DashboardPage.tsx
État actuel : 30%
Cible : 100%
Timeline : Mer 16 - Jeu 17 déc (5h)
Dépendances : Dashboard components, aiService.ts

✓ Sections à implémenter:
  [ ] Welcome section
  [ ] Stats cards (courses, progress, etc)
  [ ] Recent activity feed
  [ ] Study progress chart
  [ ] Upcoming deadlines
  [ ] Recommendations widget
  [ ] Quick actions
  [ ] Performance metrics

✓ Data sources:
  [ ] User profile data
  [ ] Enrollment history
  [ ] Progress tracking
  [ ] AI recommendations
```

### Priorité IMPORTANTE

#### ⬜ P4. Profile.tsx
```
Localisation : frontend/src/pages/Profile.tsx
État actuel : 40%
Cible : 100%
Timeline : Ven 18 - Sam 19 déc (4h)
Dépendances : Auth services, types/index.ts

✓ Sections à implémenter:
  [ ] Profile header (avatar, name, role)
  [ ] Personal information (edit form)
  [ ] Account settings
  [ ] Privacy settings
  [ ] Security settings (password change)
  [ ] Notification preferences
  [ ] Billing information
  [ ] Delete account option
```

#### ⬜ P5. Admin/Users.tsx
```
Localisation : frontend/src/pages/admin/Users.tsx
État actuel : 0%
Cible : 80% (minimal)
Timeline : Lun 21 - Mar 22 déc (3h)

✓ Fonctionnalités:
  [ ] Users list with pagination
  [ ] Search & filter
  [ ] User details modal
  [ ] Deactivate/Reactivate user
  [ ] View user orders
```

#### ⬜ P6. Admin/Subjects.tsx
```
Localisation : frontend/src/pages/admin/Subjects.tsx
État actuel : 0%
Cible : 80% (minimal)
Timeline : Lun 21 - Mar 22 déc (3h)

✓ Fonctionnalités:
  [ ] Subjects list
  [ ] Create/Edit subject
  [ ] Delete subject
  [ ] Publish/Unpublish
  [ ] View enrollments
```

#### ⬜ P7. Admin/Orders.tsx
```
Localisation : frontend/src/pages/admin/Orders.tsx
État actuel : 0%
Cible : 80% (minimal)
Timeline : Mer 23 - Jeu 24 déc (2h)

✓ Fonctionnalités:
  [ ] Orders list with filters
  [ ] Order details view
  [ ] Order status tracking
  [ ] Refund management
```

### Priorité OPTIONNELLE (Post-MVP)

#### ⬜ P8-12. Pages statiques
```
- About.tsx (0%)
- Contact.tsx (0%)
- FAQ.tsx (0%)
- Terms.tsx (0%)
- Privacy.tsx (0%)
```

---

## 🧩 COMPONENTS (15 tâches)

### Dashboard Components (À compléter)

#### ⬜ C1. StatsCard.tsx
```
État : 50% → 100%
Timeline : Jeu 10 déc (1h)
Props: title, value, change, icon

✓ À ajouter:
  [ ] Trend indicator (up/down)
  [ ] Sparkline chart
  [ ] Tooltip on hover
  [ ] Color coding
```

#### ⬜ C2. RecentActivity.tsx
```
État : 30% → 100%
Timeline : Jeu 10 déc (1.5h)

✓ À ajouter:
  [ ] Activity list rendering
  [ ] Timestamp formatting
  [ ] Action icons
  [ ] Pagination
```

#### ⬜ C3. StudyProgress.tsx
```
État : 40% → 100%
Timeline : Jeu 10 déc (2h)

✓ À ajouter:
  [ ] Progress bars per course
  [ ] Percentage display
  [ ] Time estimates
  [ ] Next milestone
```

### Common UI Components (À créer)

#### ⬜ C4. Skeleton.tsx
```
Timeline : Jeu 10 déc (1h)
Usage: Loading placeholders

✓ Variants:
  [ ] SkeletonText
  [ ] SkeletonImage
  [ ] SkeletonCard
  [ ] Animation effect
```

#### ⬜ C5. Tooltip.tsx
```
Timeline : Jeu 10 déc (1h)
Props: content, position, delay

✓ Fonctionnalités:
  [ ] Position auto-adjust
  [ ] Keyboard accessible
  [ ] Animation
```

#### ⬜ C6. Dropdown.tsx
```
Timeline : Jeu 10 déc (1.5h)
Props: items, onSelect, placeholder

✓ Fonctionnalités:
  [ ] Keyboard navigation
  [ ] Search filtering
  [ ] Multi-select option
  [ ] Accessibility
```

#### ⬜ C7-15. Autres components
```
- Breadcrumb.tsx (timeline)
- EmptyState.tsx (complétion)
- Accordion.tsx (création)
- etc.
```

---

## 🧪 TESTS (5 tâches)

#### ⬜ T1. Unit Tests - Services
```
Timeline : Mer 16 déc (4h)
Coverage: 80% minimum

✓ À tester:
  [ ] cartService.ts (toutes les fonctions)
  [ ] paymentService.ts (happy + error paths)
  [ ] authService.ts (existing)
  [ ] catalogService.ts (existing)

Tools: Vitest + React Testing Library
```

#### ⬜ T2. Integration Tests - Workflows
```
Timeline : Jeu 17 - Ven 18 déc (4h)

✓ Workflows à tester:
  [ ] Add to cart → Checkout → Payment
  [ ] Login → Dashboard → View course → Add to cart
  [ ] Search → Filter → View details → Add to favorites
```

#### ⬜ T3. E2E Tests - User Journeys
```
Timeline : Lun 21 - Mar 22 déc (4h)
Tool: Playwright ou Cypress (TBD)

✓ Journeys:
  [ ] New user registration → Course discovery
  [ ] Existing user → Login → Purchase → Dashboard
```

#### ⬜ T4. Performance Tests
```
Timeline : Mer 23 déc (2h)

✓ Métriques:
  [ ] Lighthouse score > 90
  [ ] Core Web Vitals
  [ ] Bundle size < 300KB
  [ ] Load time < 3s
```

#### ⬜ T5. Accessibility Tests
```
Timeline : Jeu 24 déc (2h)

✓ Vérifications:
  [ ] WCAG 2.1 AA compliance
  [ ] Keyboard navigation
  [ ] Screen reader compatibility
  [ ] Color contrast
```

---

## 🐛 BUG FIXES & OPTIMIZATIONS (As needed)

#### Semaine 1-2
```
[ ] Memory leak investigation
[ ] Context provider optimization
[ ] CSS module organization
[ ] Responsive design issues
```

#### Semaine 3
```
[ ] Performance bottlenecks
[ ] Browser compatibility
[ ] Mobile layout issues
[ ] Accessibility violations
```

---

## 📋 CHECKLIST FINALE PRÉ-RELEASE

### Code Quality
```
[ ] ESLint errors : 0
[ ] TypeScript errors : 0
[ ] Console warnings : 0 (production)
[ ] No console.log() in production code
[ ] Code formatted with prettier
```

### Testing
```
[ ] Unit test coverage : > 80%
[ ] All critical paths tested
[ ] No failing tests
[ ] E2E tests passing
[ ] Performance tests passing
```

### Documentation
```
[ ] JSDoc comments on all exports
[ ] README updated
[ ] API documentation complete
[ ] Setup guide accurate
[ ] Environment variables documented
```

### Build & Deploy
```
[ ] Production build successful
[ ] Build size within limits
[ ] Source maps generated
[ ] .env.example updated
[ ] Deployment script tested
```

### Security
```
[ ] No hardcoded secrets
[ ] HTTPS enforced
[ ] CORS properly configured
[ ] Input validation complete
[ ] SQL injection prevention (if applicable)
[ ] XSS prevention in place
```

### Browser/Device Testing
```
[ ] Chrome (latest)
[ ] Firefox (latest)
[ ] Safari (latest)
[ ] Mobile (iOS Safari, Chrome Mobile)
[ ] Tablet responsive
[ ] Dark mode working
```

### Product
```
[ ] Feature requirements met
[ ] No critical bugs
[ ] User experience smooth
[ ] Error messages clear
[ ] Loading states present
[ ] Empty states handled
```

---

## 📊 TRACKING

### Weekly Progress (à mettre à jour)

**Semaine 1 (7-13 déc)**
```
Services : [ ] [ ] [ ] [ ] [ ] [ ] [ ] [ ] 0/8
Expected : 5/8 (62%)
Actual : ___%
Blocker : ________
```

**Semaine 2 (14-20 déc)**
```
Pages : [ ] [ ] [ ] [ ] [ ] [ ] [ ] [ ] 0/7
Components : [ ] [ ] [ ] 0/3
Expected : 7/10 (70%)
Actual : ___%
Blocker : ________
```

**Semaine 3 (21-27 déc)**
```
Tests : [ ] [ ] [ ] [ ] [ ] 0/5
Fixes : [ ] [ ] [ ] 0/3
Expected : 100%
Actual : ___%
Blocker : ________
```

---

## 🎉 GO/NO-GO DECISION (27 déc)

### Release Readiness Checklist

```
MUST-HAVE (tous requis pour GO):
[ ] All critical services implemented
[ ] All critical pages complete
[ ] No critical bugs
[ ] Performance targets met
[ ] Security audit passed

SHOULD-HAVE (au moins 90%):
[ ] 80%+ of optional features
[ ] 80%+ test coverage
[ ] 90%+ Lighthouse score

NICE-TO-HAVE (optionnel):
[ ] Admin dashboard features
[ ] Analytics integration
[ ] Advanced AI features
```

### Release Decision
```
GO / NO-GO : _________
Decision Date : 27 déc 2025
Approved By : __________
```

---

**Checklist créée** : 6 décembre 2025  
**À commencer** : 7 décembre 2025  
**Deadline** : 27 décembre 2025

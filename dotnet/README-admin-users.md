# Gestion des utilisateurs — admin

Fichiers **prêts à copier**, aucune modification manuelle à faire.

| Fichier fourni | Destination | Nature |
| --- | --- | --- |
| `Controllers/AdminUsersController.cs` | `dotnet/Controllers/AdminUsersController.cs` | nouveau |
| `Middlewares/PresenceTrackingMiddleware.cs` | `dotnet/Middlewares/PresenceTrackingMiddleware.cs` | nouveau |
| `Controllers/AdminController.cs` | `dotnet/Controllers/AdminController.cs` | **remplace** l'existant |
| `Program.cs` | `dotnet/Program.cs` | **remplace** l'existant |

Aucune migration EF : `Users`, `UserSessions`, `Subscriptions`, `Payments`,
`Orders`, `PricingPlans`, `RefreshTokens` et `PasswordResetTokens` existent déjà
et sont exposés en `DbSet` par `ApplicationDbContext`.

```bash
cd winplus-api/dotnet
dotnet build
dotnet run
```

## Ce qui a été corrigé dans `AdminController.cs`

`AdminUsersController` prend en charge toutes les routes `api/admin/users…`.
Les dix méthodes homonymes ont été **supprimées** d'`AdminController.cs`
(384 lignes retirées, 1 777 → 1 393), ce qui évite l'`AmbiguousMatchException`
qu'ASP.NET lève au démarrage sur deux méthodes déclarant la même route :

| Attribut supprimé | Méthode | Reprise dans le nouveau controller |
| --- | --- | --- |
| `[HttpGet("users")]` | `GetAllUsers` | `List` — + filtres rôle, statut, paiement, présence |
| `[HttpPut("users/{id}/role")]` | `UpdateUserRole` | `SetRole` — + garde-fou anti-auto-déclassement |
| `[HttpPut("users/{id}/status")]` | `UpdateUserStatus` | `Update` (champ `status`) |
| `[HttpPut("users/{id}")]` | `UpdateUser` | `Update` |
| `[HttpPost("users/{id}/suspend")]` | `SuspendUser` | `Suspend` — + révocation des sessions |
| `[HttpPost("users/{id}/reactivate")]` | `ReactivateUser` | `Reactivate` |
| `[HttpDelete("users/{id}")]` | `SoftDeleteUser` | `SoftDelete` |
| `[HttpPost("users/{id}/restore")]` | `RestoreUser` | `Restore` (alias de `Reactivate`) |
| `[HttpDelete("users/{id}/hard")]` | `HardDeleteUser` | `HardDelete` — transactionnel |
| `[HttpPost("users/{id}/delete")]` | `DeleteUser` | `SoftDeletePost` (alias) |

Toutes les routes, alias historiques compris, sont reprises : aucun client
existant ne casse. Les routes `user/{userId}/block` et `user/{userId}/unblock`
(préfixe singulier `user/`) n'étaient pas en conflit et ont été conservées
telles quelles. Le reste d'`AdminController.cs` — analytics, contenus,
commandes, chat, WinAI, logs, santé système — est inchangé.

Les DTO `AdminUserListResponse`, `AdminUserResponse`, `UpdateUserRoleRequest` et
`UpdateUserStatusRequest` ne sont plus utilisés par ce controller mais restent
dans `Models/DTOs` : d'autres controllers peuvent s'en servir, et un type non
référencé ne gêne pas la compilation.

## Ce qui a été corrigé dans `Program.cs`

Une ligne ajoutée après `app.UseAuthorization()` :

```csharp
app.UsePresenceTracking();
```

`using Backend.Middlewares;` était déjà présent en tête de fichier. Rien d'autre
n'a été touché.

Ce middleware met à jour `UserSessions.LastActivityAt` à chaque requête
authentifiée (une écriture par minute et par session au maximum, anti-rebond en
mémoire). Sans lui, `isOnline` reste faux pour tout le monde et la vue
« Utilisateurs connectés » reste vide.

## Endpoints exposés

| Méthode | Route | Rôle |
| --- | --- | --- |
| GET | `/api/admin/users` | Liste paginée. `q`, `role`, `status`, `payment`, `online`, `page`, `pageSize`, `sort` |
| GET | `/api/admin/users/stats` | Total, en ligne, actifs, suspendus, abonnés, non payés, nouveaux du jour, revenu |
| GET | `/api/admin/users/online` | Comptes avec session active (< 5 min) |
| GET | `/api/admin/users/{id}` | Fiche complète |
| POST | `/api/admin/users` | Création, invitation email optionnelle |
| PUT | `/api/admin/users/{id}` | Nom, email, téléphone, rôle, statut |
| PUT | `/api/admin/users/{id}/role` | Rôle seul |
| POST | `/api/admin/users/{id}/suspend` | Suspension + révocation des sessions |
| POST | `/api/admin/users/{id}/reactivate` | Réactivation |
| POST | `/api/admin/users/{id}/restore` | Alias de réactivation |
| DELETE | `/api/admin/users/{id}` | Suppression douce, restaurable |
| POST | `/api/admin/users/{id}/delete` | Alias de suppression douce |
| DELETE | `/api/admin/users/{id}/hard` | Suppression définitive |
| POST | `/api/admin/users/{id}/verify-email` | Valide l'email manuellement |
| POST | `/api/admin/users/{id}/reset-password` | Envoie un lien de réinitialisation |
| GET | `/api/admin/users/{id}/sessions` | Appareil, navigateur, IP, localisation |
| DELETE | `/api/admin/users/{id}/sessions` | Déconnexion forcée (sessions + refresh tokens) |
| GET | `/api/admin/users/{id}/payments` | Historique des paiements |
| GET | `/api/admin/users/{id}/activity` | Connexions, commandes, paiements |
| POST | `/api/admin/users/{id}/grant-subscription` | Octroi manuel d'abonnement |

## Calcul de l'état de paiement

Dans l'ordre, sans valeur inventée :

1. Abonnement le plus récent non supprimé :
   - `Status = "trial"` → **Essai**
   - `Status = "refunded"` → **Remboursé**
   - `Status = "active"` et `EndDate` nulle ou future → **À jour**
   - `Status = "cancelled"` mais `EndDate` future → **À jour** (résilié, encore couvert)
   - sinon → **Expiré**
2. Aucun abonnement : somme des `Payments` au statut `completed`.
   Supérieure à zéro → **À jour**, sinon → **Non payé**.

`TotalPaid` est la somme des paiements `completed`, `OrdersCount` le nombre de
commandes non supprimées. La colonne « Encaissé » est donc la réalité comptable.

## Garde-fous

- Un admin ne peut ni se suspendre, ni se supprimer, ni retirer son propre rôle admin.
- La suspension révoque immédiatement sessions et refresh tokens.
- La suppression définitive purge sessions et tokens mais **détache** paiements et
  commandes (`UserId = null`) au lieu de les effacer, pour préserver la
  comptabilité. Le tout dans une transaction, avec rollback en cas d'échec.
- Un échec d'envoi d'email n'annule jamais la création du compte.
- Création sans mot de passe fourni : un secret aléatoire est généré et jamais
  communiqué, l'accès passe par le lien d'invitation.

## Note de performance

`online` et `payment` portent sur des agrégats et sont appliqués après
projection, sur un lot borné à 5 000 comptes. Au-delà, matérialisez la présence
et l'état d'abonnement en colonnes sur `Users` (`LastSeenAt`,
`SubscriptionState`, alimentées par le middleware et le webhook de paiement),
puis remplacez le post-filtrage par un `Where` SQL.

## Vérification

Avec un jeton admin :

```bash
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/admin/users/stats
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/admin/users/online
curl -H "Authorization: Bearer $TOKEN" "http://localhost:5000/api/admin/users?payment=unpaid&pageSize=5"
```

Côté frontend, rien à modifier : `src/services/adminUserService.ts` appelle déjà
exactement ces routes, et `AdminUsers` / `AdminOnlineUsers` sont branchés dans
`src/pages/AdminDashboard.tsx` sur les vues `users` et `online`.

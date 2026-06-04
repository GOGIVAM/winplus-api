# Frontend Migration Guide - Custom Authentication

## 📋 Status: IN PROGRESS
- ✅ AuthContext.tsx created (useAuth hook)
- ✅ Signup.tsx updated (custom auth)
- ✅ Login.tsx updated (custom auth)
- ⏳ VerifyEmail.tsx - needs update
- ⏳ ForgotPassword.tsx - needs update
- ⏳ ResetPassword.tsx - needs update
- ⏳ App.tsx - needs AuthProvider wrapper
- ⏳ .env.production - ready

---

## 🔄 Steps to Migrate Frontend

### Step 1: Replace AuthContext Provider in App.tsx or main.tsx

**Location:** `frontend/src/App.tsx` or `frontend/src/main.tsx`

```tsx
import { AuthProvider } from './contexts/AuthContext';

// Wrap your entire app with AuthProvider
function App() {
  return (
    <AuthProvider>
      {/* Rest of your app */}
      <Routes>
        {/* Your routes */}
      </Routes>
    </AuthProvider>
  );
}

export default App;
```

### Step 2: Update Email Verification Page

**File:** `frontend/src/pages/EmailVerification.tsx` or `frontend/src/pages/VerifyEmail.tsx`

Replace Cognito context with:

```tsx
import { useAuth } from '../contexts/AuthContext';

export const VerifyEmail = () => {
  const { verifyEmail, resendVerificationEmail, user } = useAuth();
  
  // Use verifyEmail(userId, code) and resendVerificationEmail(email)
};
```

### Step 3: Update Password Reset Flows

**Files to update:**
- `frontend/src/pages/ForgotPassword.tsx`
- `frontend/src/pages/ResetPassword.tsx`

Replace with:

```tsx
import { useAuth } from '../contexts/AuthContext';

export const ForgotPassword = () => {
  const { forgotPassword } = useAuth();
  // Use forgotPassword(email)
};

export const ResetPassword = () => {
  const { resetPassword } = useAuth();
  // Use resetPassword(resetToken, newPassword)
};
```

### Step 4: Remove Cognito References

Delete or disable:
- `frontend/src/contexts/CognitoAuthContext.tsx`
- `frontend/src/services/cognitoService.ts`
- Remove Cognito dependencies from package.json:

```bash
npm uninstall amazon-cognito-identity-js @aws-amplify/ui-react aws-amplify
```

### Step 5: Update API Services

**File:** `frontend/src/services/api.ts`

Ensure axios interceptors are properly configured:

```tsx
import axios from 'axios';

const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'https://api.winplus.com',
  timeout: parseInt(import.meta.env.VITE_API_TIMEOUT || '30000'),
});

// Interceptors are handled in AuthContext.tsx
export default api;
```

### Step 6: Protected Routes

Update your route protection to use the new `useAuth` hook:

```tsx
import { useAuth } from '../contexts/AuthContext';
import { Navigate } from 'react-router-dom';

interface PrivateRouteProps {
  children: React.ReactNode;
}

export const PrivateRoute: React.FC<PrivateRouteProps> = ({ children }) => {
  const { isAuthenticated, isLoading } = useAuth();

  if (isLoading) return <div>Loading...</div>;
  
  return isAuthenticated ? <>{children}</> : <Navigate to="/login" />;
};
```

---

## 📝 Updated Pages

| Page | File | Status |
|------|------|--------|
| Login | `/pages/Login.tsx` | ✅ Updated |
| Signup | `/pages/Signup.tsx` | ✅ Updated |
| Email Verification | `/pages/EmailVerification.tsx` | ⏳ TODO |
| Forgot Password | `/pages/ForgotPassword.tsx` | ⏳ TODO |
| Reset Password | `/pages/ResetPassword.tsx` | ⏳ TODO |
| Profile/Settings | `/pages/Profile.tsx` | ⏳ TODO |

---

## 🧠 useAuth Hook Usage

### Available Methods:

```typescript
interface AuthContextType {
  // State
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  error: string | null;

  // Methods
  signup(email, password, firstName, lastName, phone?) => Promise<void>;
  signin(email, password, rememberMe?) => Promise<void>;
  verifyEmail(userId, verificationCode) => Promise<void>;
  resendVerificationEmail(email) => Promise<void>;
  forgotPassword(email) => Promise<void>;
  resetPassword(resetToken, newPassword) => Promise<void>;
  changePassword(currentPassword, newPassword) => Promise<void>;
  logout() => Promise<void>;
  refreshToken() => Promise<void>;
  
  // Utilities
  clearError() => void;
  isEmailVerified: boolean;
}
```

### Example Usage:

```tsx
import { useAuth } from '../contexts/AuthContext';

function MyComponent() {
  const { user, isAuthenticated, signin, logout, error } = useAuth();

  const handleLogin = async () => {
    try {
      await signin('user@example.com', 'password123', true); // rememberMe=true
    } catch (err) {
      console.error(err);
    }
  };

  return (
    <div>
      {isAuthenticated && <p>Welcome, {user?.firstName}!</p>}
      <button onClick={handleLogin}>Login</button>
      <button onClick={logout}>Logout</button>
    </div>
  );
}
```

---

## 🔧 Environment Variables

### .env.production (Already Created)

```env
VITE_API_BASE_URL=https://api.winplus.com
VITE_USE_CUSTOM_AUTH=true
VITE_JWT_STORAGE_KEY=winplus_access_token
VITE_REFRESH_STORAGE_KEY=winplus_refresh_token
VITE_REMEMBER_ME_DAYS=30
```

### .env.development (Update if needed)

```env
VITE_API_BASE_URL=http://localhost:5001
VITE_USE_CUSTOM_AUTH=true
```

---

## 🧪 Testing Checklist

- [ ] Login page loads without Cognito errors
- [ ] Signup creates new user and sends email
- [ ] Email verification code works (6 digits)
- [ ] Remember Me checkbox extends session 30 days
- [ ] Forgot Password emails work
- [ ] Reset Password with token works
- [ ] Logout clears tokens properly
- [ ] Token refresh works after expiry
- [ ] Protected pages redirect to /login when not authenticated
- [ ] User profile shows correct data

---

## 🚀 Production Deployment

### Pre-Deployment:

1. ✅ Remove all Cognito dependencies
2. ✅ Test all auth flows locally
3. ✅ Verify .env.production has correct API URL
4. ✅ Run frontend build: `npm run build`
5. ✅ Check bundle size (Cognito removal should reduce it)

### Deploy:

```bash
# Build
npm run build

# Deploy dist/ folder to:
# - AWS S3 + CloudFront
# - Vercel
# - Netlify
# - Your own server

# Ensure CORS headers on backend permit frontend domain
```

### Post-Deployment:

1. Test login/signup on production domain
2. Monitor error logs (Sentry/LogRocket)
3. Verify SendGrid emails arrive correctly
4. Test Remember Me across devices
5. Check JWT token expiry/refresh flow

---

## 📚 Files Reference

### New Context File:
- `frontend/src/contexts/AuthContext.tsx` - Complete auth provider with JWT management

### Updated Page Files:
- `frontend/src/pages/Login.tsx` - Uses `useAuth().signin()`
- `frontend/src/pages/Signup.tsx` - Uses `useAuth().signup()`

### Files Still to Update:
- `frontend/src/pages/VerifyEmail.tsx` - Use `useAuth().verifyEmail()`
- `frontend/src/pages/ForgotPassword.tsx` - Use `useAuth().forgotPassword()`
- `frontend/src/pages/ResetPassword.tsx` - Use `useAuth().resetPassword()`
- `frontend/src/App.tsx` or `main.tsx` - Wrap with `<AuthProvider>`

---

## 🔐 Security Notes

- ✅ JWT tokens stored in localStorage (consider sessionStorage for higher security)
- ✅ Refresh token rotation on each refresh
- ✅ Automatic token refresh on 401 response
- ✅ Tokens cleared on logout and on 401 permanent failure
- ⚠️ TODO: Consider adding token blacklist on logout

---

## 🆘 Common Issues

### Issue: "Cannot find module '@aws-amplify'"
**Solution:** Remove AWS Amplify from imports and use `useAuth` hook instead

### Issue: "Tokens not persisting on page reload"
**Solution:** Tokens are stored in localStorage - check browser storage tab in DevTools

### Issue: "401 Unauthorized after login"
**Solution:** Ensure JWT token is being sent in Authorization header - check Network tab in DevTools

### Issue: "Email not sending after signup"
**Solution:** Check SendGrid API configuration in appsettings.json - verify API key is valid

---

## 📞 Quick Reference

**API Endpoints (all prefixed with /api/auth):**
- POST /signup - Create account
- POST /signin - Login
- POST /verify-email - Verify with code
- POST /refresh-token - Get new tokens
- POST /logout - Revoke token
- POST /forgot-password - Request reset
- POST /reset-password - Complete reset
- POST /change-password - Change (authenticated)
- GET /me - Get current user

---

**Version:** 1.0  
**Last Updated:** 6 février 2026  
**Status:** In Progress - 40% Frontend Migration Complete

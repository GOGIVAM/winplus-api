#!/usr/bin/env python3
"""
Authentification JWT pour FastAPI
Partage le même secret que .NET backend
"""

import jwt
import os
from typing import Optional, List
from fastapi import Depends, HTTPException, status
from fastapi.security import HTTPBearer, HTTPAuthCredentials
import logging

logger = logging.getLogger(__name__)

# Configuration JWT (même que .NET)
JWT_SECRET = os.getenv('JWT_SECRET_KEY', 'your-secret-key-must-match-dotnet')
JWT_ALGORITHM = 'HS256'

# Security scheme
security = HTTPBearer()


class UserTokenData:
    """Container pour les données utilisateur extraites du token"""
    def __init__(self, user_id: int, email: str, role: str):
        self.user_id = user_id
        self.email = email
        self.role = role


async def verify_token(credentials: HTTPAuthCredentials = Depends(security)) -> UserTokenData:
    """
    Dépendance FastAPI pour vérifier le token JWT
    Utilisation: @app.get("/protected") def endpoint(user = Depends(verify_token))
    """
    token = credentials.credentials
    
    try:
        # Valider JWT avec même secret que .NET
        payload = jwt.decode(
            token,
            JWT_SECRET,
            algorithms=[JWT_ALGORITHM]
        )
        
        # Extraire informations utilisateur
        user_id = payload.get('user_id')
        email = payload.get('email')
        role = payload.get('role', 'student')
        
        # Vérifier que user_id est présent
        if not user_id:
            logger.warning("Token does not contain user_id")
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Token does not contain user_id",
                headers={"WWW-Authenticate": "Bearer"},
            )
        
        logger.info(f"✅ Authenticated request from user_id={user_id}, role={role}")
        
        return UserTokenData(
            user_id=user_id,
            email=email,
            role=role
        )
        
    except jwt.ExpiredSignatureError:
        logger.warning("Token expired")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Your session has expired, please login again",
            headers={"WWW-Authenticate": "Bearer"},
        )
    except jwt.InvalidTokenError as e:
        logger.error(f"Invalid token: {e}")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Authentication token is invalid",
            headers={"WWW-Authenticate": "Bearer"},
        )
    except Exception as e:
        logger.error(f"Token validation error: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail="Authentication failed"
        )


def require_role(*allowed_roles: str):
    """
    Dépendance pour vérifier le rôle utilisateur
    Usage: 
        async def protected_endpoint(user: UserTokenData = Depends(require_role('admin', 'teacher'))):
            ...
    """
    async def role_checker(user: UserTokenData = Depends(verify_token)) -> UserTokenData:
        if user.role not in allowed_roles:
            logger.warning(f"Access forbidden for user {user.user_id}. Required roles: {allowed_roles}")
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail=f"Access forbidden. Required roles: {', '.join(allowed_roles)}"
            )
        return user
    
    return role_checker

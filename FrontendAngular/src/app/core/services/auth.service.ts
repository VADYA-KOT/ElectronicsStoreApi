
import {
  Injectable,
  inject,
  signal,
  computed
} from '@angular/core';

import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

import {
  LoginRequest,
  RegisterRequest,
  RegisterResponse,
  AuthTokenResponse
} from '../models/auth.model';

import { UserProfileResponse } from '../models/user-profile.model';

import { API_BASE_URL } from '../config/api.config';

const AUTH_STORAGE_KEY = 'electronics-store-auth';

const ROLE_CLAIM =
  'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

interface StoredAuth {
  accessToken: string;
  expiresAtUtc: string;
}

interface JwtPayload {
  [key: string]: unknown;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly authState = signal<StoredAuth | null>(
    this.readStoredAuth()
  );

  // ID текущего авторизованного пользователя.
  readonly currentUserId = computed<number | null>(() => {
    const auth = this.authState();

    if (!auth) {
      return null;
    }

    const expiresAt = Date.parse(auth.expiresAtUtc);

    if (Number.isNaN(expiresAt) || expiresAt <= Date.now()) {
      return null;
    }

    const payload = this.decodeTokenPayload(auth.accessToken);
    const subject = payload?.['sub'];

    if (
      typeof subject !== 'string' &&
      typeof subject !== 'number'
    ) {
      return null;
    }

    const userId = Number(subject);

    return Number.isSafeInteger(userId) && userId > 0
      ? userId
      : null;
  });

  // Проверка авторизации.
  get isAuthenticated(): boolean {
    return this.getAccessToken() !== null;
  }

  // Проверка роли администратора.
  get isAdmin(): boolean {
    const token = this.getAccessToken();

    if (!token) {
      return false;
    }

    const payload = this.decodeTokenPayload(token);

    if (!payload) {
      return false;
    }

    const roleClaim =
      payload[ROLE_CLAIM] ??
      payload['role'] ??
      payload['roles'];

    if (typeof roleClaim === 'string') {
      return roleClaim.toLowerCase() === 'admin';
    }

    if (Array.isArray(roleClaim)) {
      return roleClaim.some(
        role =>
          typeof role === 'string' &&
          role.toLowerCase() === 'admin'
      );
    }

    return false;
  }

  // Регистрация пользователя.
  register(request: RegisterRequest): Observable<RegisterResponse> {
    return this.http.post<RegisterResponse>(
      `${API_BASE_URL}/api/auth/register`,
      request
    );
  }

  // Вход пользователя.
  login(request: LoginRequest): Observable<AuthTokenResponse> {
    return this.http
      .post<AuthTokenResponse>(
        `${API_BASE_URL}/api/auth/login`,
        request
      )
      .pipe(
        tap(response => this.saveAuth(response))
      );
  }

  // Получение профиля текущего пользователя.
  getCurrentProfile(): Observable<UserProfileResponse> {
    return this.http.get<UserProfileResponse>(
      `${API_BASE_URL}/api/auth/me`
    );
  }

  // Изменение пароля текущего пользователя.
  changePassword(
    currentPassword: string,
    newPassword: string
  ): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(
      `${API_BASE_URL}/api/auth/change-password`,
      {
        currentPassword,
        newPassword
      }
    );
  }

  // Получение действующего токена.
  getAccessToken(): string | null {
    const auth = this.authState();

    if (!auth) {
      return null;
    }

    if (Date.parse(auth.expiresAtUtc) <= Date.now()) {
      this.logout();
      return null;
    }

    return auth.accessToken;
  }

  // Выход пользователя.
  logout(): void {
    this.authState.set(null);

    try {
      sessionStorage.removeItem(AUTH_STORAGE_KEY);
    } catch (error: unknown) {
      console.error('Не удалось удалить данные авторизации:', error);
    }
  }

  // Сохранение JWT.
  private saveAuth(response: AuthTokenResponse): void {
    const auth: StoredAuth = {
      accessToken: response.accessToken,
      expiresAtUtc: response.expiresAtUtc
    };

    this.authState.set(auth);

    try {
      sessionStorage.setItem(
        AUTH_STORAGE_KEY,
        JSON.stringify(auth)
      );
    } catch (error: unknown) {
      console.error('Не удалось сохранить данные авторизации:', error);
    }
  }

  // Чтение полезной нагрузки JWT.
  private decodeTokenPayload(token: string): JwtPayload | null {
    try {
      const parts = token.split('.');

      if (parts.length !== 3) {
        return null;
      }

      const base64 = parts[1]
        .replace(/-/g, '+')
        .replace(/_/g, '/');

      const paddedBase64 = base64.padEnd(
        Math.ceil(base64.length / 4) * 4,
        '='
      );

      const binary = atob(paddedBase64);

      const bytes = Uint8Array.from(
        binary,
        character => character.charCodeAt(0)
      );

      const decoded = new TextDecoder().decode(bytes);

      const payload: unknown = JSON.parse(decoded);

      if (
        typeof payload !== 'object' ||
        payload === null ||
        Array.isArray(payload)
      ) {
        return null;
      }

      return payload as JwtPayload;
    } catch {
      return null;
    }
  }

  // Загрузка сохранённой авторизации.
  private readStoredAuth(): StoredAuth | null {
    try {
      if (typeof sessionStorage === 'undefined') {
        return null;
      }

      const savedData = sessionStorage.getItem(AUTH_STORAGE_KEY);

      if (!savedData) {
        return null;
      }

      const parsed: unknown = JSON.parse(savedData);

      if (typeof parsed !== 'object' || parsed === null) {
        return null;
      }

      const auth = parsed as Record<string, unknown>;

      if (
        typeof auth['accessToken'] !== 'string' ||
        typeof auth['expiresAtUtc'] !== 'string'
      ) {
        return null;
      }

      const expiresAt = Date.parse(auth['expiresAtUtc']);

      if (Number.isNaN(expiresAt) || expiresAt <= Date.now()) {
        sessionStorage.removeItem(AUTH_STORAGE_KEY);
        return null;
      }

      return {
        accessToken: auth['accessToken'],
        expiresAtUtc: auth['expiresAtUtc']
      };
    } catch (error: unknown) {
      console.error('Не удалось загрузить авторизацию:', error);
      return null;
    }
  }
}

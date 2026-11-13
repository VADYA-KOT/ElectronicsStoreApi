
import {
  Component,
  OnInit,
  inject,
  signal
} from '@angular/core';

import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../core/services/auth.service';
import { UserProfileResponse } from '../../core/models/user-profile.model';

@Component({
  selector: 'app-account',
  standalone: true,
  imports: [
    DatePipe,
    RouterLink
  ],
  templateUrl: './account.component.html',
  styleUrl: './account.component.css'
})
export class AccountComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly profile = signal<UserProfileResponse | null>(null);
  readonly isLoading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  // Поля смены пароля.
  readonly currentPassword = signal('');
  readonly newPassword = signal('');
  readonly confirmNewPassword = signal('');

  // Состояние отправки запроса.
  readonly isChangingPassword = signal(false);

  // Сообщения формы.
  readonly passwordErrorMessage = signal<string | null>(null);
  readonly passwordSuccessMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.loadProfile();
  }

  // Загрузка профиля с сервера.
  private loadProfile(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.authService.getCurrentProfile().subscribe({
      next: profile => {
        this.profile.set(profile);
        this.isLoading.set(false);
      },

      error: (error: unknown) => {
        this.isLoading.set(false);

        if (
          error instanceof HttpErrorResponse &&
          error.status === 401
        ) {
          this.authService.logout();

          void this.router.navigate(['/auth'], {
            queryParams: {
              returnUrl: '/account'
            }
          });

          return;
        }

        this.errorMessage.set(
          this.getApiErrorMessage(
            error,
            'Не удалось загрузить профиль пользователя.'
          )
        );
      }
    });
  }

  // Изменение текущего пароля.
  onCurrentPasswordChange(event: Event): void {
    const input = event.target as HTMLInputElement;

    this.currentPassword.set(input.value);
    this.clearPasswordMessages();
  }

  // Изменение нового пароля.
  onNewPasswordChange(event: Event): void {
    const input = event.target as HTMLInputElement;

    this.newPassword.set(input.value);
    this.clearPasswordMessages();
  }

  // Повтор нового пароля.
  onConfirmNewPasswordChange(event: Event): void {
    const input = event.target as HTMLInputElement;

    this.confirmNewPassword.set(input.value);
    this.clearPasswordMessages();
  }

  // Смена пароля.
  changePassword(event: Event): void {
    event.preventDefault();

    if (this.isChangingPassword()) {
      return;
    }

    this.clearPasswordMessages();

    const currentPassword = this.currentPassword();
    const newPassword = this.newPassword();
    const confirmNewPassword = this.confirmNewPassword();

    if (!currentPassword) {
      this.passwordErrorMessage.set(
        'Введите текущий пароль.'
      );
      return;
    }

    if (newPassword.length < 12) {
      this.passwordErrorMessage.set(
        'Новый пароль должен содержать не менее 12 символов.'
      );
      return;
    }

    if (newPassword.length > 128) {
      this.passwordErrorMessage.set(
        'Новый пароль не должен превышать 128 символов.'
      );
      return;
    }

    if (newPassword !== confirmNewPassword) {
      this.passwordErrorMessage.set(
        'Новые пароли не совпадают.'
      );
      return;
    }

    this.isChangingPassword.set(true);

    this.authService.changePassword(
      currentPassword,
      newPassword
    ).subscribe({
      next: response => {
        this.isChangingPassword.set(false);

        this.currentPassword.set('');
        this.newPassword.set('');
        this.confirmNewPassword.set('');

        this.passwordSuccessMessage.set(
          response.message || 'Пароль успешно изменён.'
        );
      },

      error: (error: unknown) => {
        this.isChangingPassword.set(false);

        if (
          error instanceof HttpErrorResponse &&
          error.status === 401
        ) {
          this.authService.logout();

          void this.router.navigate(['/auth'], {
            queryParams: {
              returnUrl: '/account'
            }
          });

          return;
        }

        this.passwordErrorMessage.set(
          this.getApiErrorMessage(
            error,
            'Не удалось изменить пароль.'
          )
        );
      }
    });
  }

  // Очистка сообщений.
  private clearPasswordMessages(): void {
    this.passwordErrorMessage.set(null);
    this.passwordSuccessMessage.set(null);
  }

  // Выход из аккаунта.
  logout(): void {
    this.authService.logout();

    void this.router.navigate(['/auth']);
  }

  // Обработка ошибок API.
  private getApiErrorMessage(
    error: unknown,
    fallback: string
  ): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return 'Не удалось связаться с сервером.';
      }

      const body: unknown = error.error;

      if (
        typeof body === 'object' &&
        body !== null &&
        'message' in body &&
        typeof body.message === 'string'
      ) {
        return body.message;
      }
    }

    return fallback;
  }
}

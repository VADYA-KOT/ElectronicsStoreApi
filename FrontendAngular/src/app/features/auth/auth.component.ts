
import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-auth',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './auth.component.html',
  styleUrl: './auth.component.css'
})
export class AuthComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly isRegistering = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    username: [
      '',
      [
        Validators.required,
        Validators.minLength(3),
        Validators.maxLength(40),
        Validators.pattern(/^[a-zA-Z0-9._-]+$/)
      ]
    ],
    password: ['', [Validators.required]]
  });

  constructor() {
    this.isRegistering.set(
      this.route.snapshot.queryParamMap.get('mode') === 'register'
    );

    this.updatePasswordValidators();
  }

  // Переключение между входом и регистрацией.
  protected setMode(register: boolean): void {
    if (this.isSubmitting()) {
      return;
    }

    this.isRegistering.set(register);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.form.controls.password.reset('');

    this.updatePasswordValidators();

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        mode: register ? 'register' : null
      },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  // Отправка формы.
  protected submit(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.form.markAllAsTouched();

    if (this.form.invalid) {
      return;
    }

    this.errorMessage.set(null);
    this.successMessage.set(null);

    const request = this.form.getRawValue();

    this.isSubmitting.set(true);

    if (this.isRegistering()) {
      this.authService.register(request)
        .pipe(
          finalize(() => this.isSubmitting.set(false))
        )
        .subscribe({
          next: () => {
            this.setMode(false);
            this.form.controls.password.reset('');

            this.successMessage.set(
              'Регистрация выполнена. Теперь войдите в аккаунт.'
            );
          },
          error: (error: unknown) => {
            this.errorMessage.set(
              this.getErrorMessage(error, true)
            );
          }
        });

      return;
    }

    this.authService.login(request)
      .pipe(
        finalize(() => this.isSubmitting.set(false))
      )
      .subscribe({
        next: () => {
          this.redirectAfterLogin();
        },
        error: (error: unknown) => {
          this.errorMessage.set(
            this.getErrorMessage(error, false)
          );
        }
      });
  }

  // Проверка и обновление ограничений пароля.
  private updatePasswordValidators(): void {
    const validators = this.isRegistering()
      ? [
          Validators.required,
          Validators.minLength(12),
          Validators.maxLength(128)
        ]
      : [Validators.required];

    this.form.controls.password.setValidators(validators);

    this.form.controls.password.updateValueAndValidity({
      emitEvent: false
    });
  }

  // Определение адреса возврата после входа.
  private redirectAfterLogin(): void {
    const returnUrl =
      this.route.snapshot.queryParamMap.get('returnUrl');

    if (
      returnUrl &&
      returnUrl.startsWith('/') &&
      !returnUrl.startsWith('//')
    ) {
      void this.router.navigateByUrl(returnUrl);
      return;
    }

    void this.router.navigateByUrl('/');
  }

  // Обработка ошибок Backend.
  private getErrorMessage(
    error: unknown,
    registering: boolean
  ): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'Произошла непредвиденная ошибка.';
    }

    if (error.status === 0) {
      return 'Не удалось подключиться к серверу. Проверьте, запущен ли Backend.';
    }

    if (error.status === 409 && registering) {
      return 'Пользователь с таким именем уже существует.';
    }

    if (error.status === 401 && !registering) {
      return 'Неверное имя пользователя или пароль.';
    }

    if (error.status === 400) {
      return 'Проверьте корректность введённых данных.';
    }

    return 'Ошибка сервера. Попробуйте повторить запрос.';
  }
}

import { Injectable, signal } from '@angular/core';

export interface Notice {
  id: number;
  kind: 'ok' | 'error';
  text: string;
}

@Injectable({ providedIn: 'root' })
export class NoticeService {
  private nextId = 1;
  readonly notices = signal<Notice[]>([]);

  ok(text: string): void {
    this.push('ok', text);
  }

  error(text: string): void {
    this.push('error', text);
  }

  dismiss(id: number): void {
    this.notices.update((list) => list.filter((n) => n.id !== id));
  }

  private push(kind: Notice['kind'], text: string): void {
    const notice = { id: this.nextId++, kind, text };
    this.notices.update((list) => [...list.slice(-2), notice]);
    setTimeout(() => this.dismiss(notice.id), kind === 'error' ? 7000 : 4000);
  }
}

import { Component } from '@angular/core';
import { LanguageService } from '../../../core/services/language.service';

@Component({
  selector: 'app-language-selector',
  templateUrl: './language-selector.component.html',
  styleUrls: ['./language-selector.component.css'],
  // fallback inline styles to avoid compiler errors if external css isn't resolved
  styles: [`.lang-flags{display:flex;gap:8px;align-items:center}
            .flag-btn{background:transparent;border:1px solid transparent;padding:4px;border-radius:4px;cursor:pointer}
            .flag-btn[aria-pressed="true"]{outline:2px solid var(--field-border-focus)}
            .flag-btn img{width:26px;height:18px;display:block}`]
})
export class LanguageSelectorComponent {
  constructor(public langService: LanguageService) { }

  trocar(lang: string) {
    this.langService.trocarIdioma(lang).subscribe({
      error: (err) => console.error('Erro ao trocar idioma', err)
    });
  }

  // keep compatibility with any template still using onChange
  onChange(event: Event) {
    const select = event.target as HTMLSelectElement;
    if (select && select.value) {
      this.trocar(select.value);
    }
  }
}

import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { NotificationService } from '../../../../core/services/notification.service';
import { TranslateService } from '../../../../core/services/translate.service';
import {
  Mapeamento,
  MapeamentoCampo,
  AtualizarMapeamentoCamposPayload,
  MapeamentoCampoRow,
  MapeamentoRowErrors,
  TipoCampo,
  TIPO_CAMPO_OPTIONS
} from '../../models/mapeamento.model';
import { MapeamentoService } from '../../services/mapeamento.service';

export interface MapeamentoEditorState {
  hasChanges: boolean;
  hasErrors: boolean;
  isSaving: boolean;
}

@Component({
  selector: 'app-mapeamento-editor',
  templateUrl: './mapeamento-editor.component.html',
  styleUrl: './mapeamento-editor.component.css'
})
export class MapeamentoEditorComponent implements OnChanges {
  @Input() perfilId: number | null = null;
  @Input({ required: true }) colecaoId!: number;
  @Input() colecaoNome = '';
  @Input({ required: true }) mapeamento!: Mapeamento;
  @Input() readonly = false;
  @Input() managedActions = false;
  @Input() nivel = 1;

  @Output() camposChanged = new EventEmitter<void>();
  @Output() stateChanged = new EventEmitter<MapeamentoEditorState>();

  @ViewChild('fileInput') fileInput?: ElementRef<HTMLInputElement>;

  readonly displayedColumns: string[] = ['ordem', 'nomeCampo', 'descricaoCampo', 'ativo', 'indiceColuna', 'tipoCampo', 'formato', 'preview', 'acoes'];
  readonly tipoCampoOptions = TIPO_CAMPO_OPTIONS;

  rows: MapeamentoCampoRow[] = [];
  originalRows: MapeamentoCampoRow[] = [];
  isLoading = false;
  isSaving = false;
  isDownloadingExample = false;
  excelPreviewData: string[] = [];
  searchTerm = '';

  private readonly destroyRef = inject(DestroyRef);
  private loadRequestVersion = 0;

  constructor(
    private readonly mapeamentoService: MapeamentoService,
    private readonly notificationService: NotificationService,
    private readonly translate: TranslateService
  ) { }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['mapeamento']?.currentValue?.id) {
      this.loadCampos();
      return;
    }

    if (changes['readonly'] && this.readonly) {
      this.validateAll();
    }
  }

  get hasChanges(): boolean {
    return JSON.stringify(this.rows.map((row) => this.toComparableRow(row)))
      !== JSON.stringify(this.originalRows.map((row) => this.toComparableRow(row)));
  }

  get hasErrors(): boolean {
    return this.rows.some((row) => !!(row.errors.nomeCampo || row.errors.indiceColuna || row.errors.tipoCampo || row.errors.formato));
  }

  get isEditingLocked(): boolean {
    return this.readonly || this.isSaving;
  }

  get visibleRows(): MapeamentoCampoRow[] {
    const term = this.searchTerm.trim().toLocaleLowerCase('pt-BR');
    if (!term) {
      return this.rows;
    }

    return this.rows.filter((row) =>
      row.nomeCampo.toLocaleLowerCase('pt-BR').includes(term)
      || String(row.indiceColuna ?? '').includes(term)
    );
  }

  adicionarCampo(): void {
    if (this.isEditingLocked) {
      return;
    }

    const newRow: MapeamentoCampoRow = {
      id: null,
      nomeCampo: '',
      descricaoCampo: '',
      indiceColuna: null,
      tipoCampo: '',
      formato: '',
      ativo: true,
      isNew: true,
      previewValue: '',
      errors: this.emptyErrors()
    };

    this.rows = [...this.rows, newRow];
    this.emitState();
  }

  removerCampo(index: number): void {
    if (this.isEditingLocked) {
      return;
    }

    this.rows = this.rows.filter((_, currentIndex) => currentIndex !== index);
    this.validateAll();
    this.refreshPreview();
    this.emitState();
  }

  removerCampoRow(row: MapeamentoCampoRow): void {
    const index = this.rows.indexOf(row);
    if (index >= 0) {
      this.removerCampo(index);
    }
  }

  onFieldChange(row: MapeamentoCampoRow): void {
    if (this.isEditingLocked) {
      return;
    }

    if (row.tipoCampo !== TipoCampo.DateTime) {
      row.formato = '';
    }

    this.validateRow(row);
    this.refreshPreview();
    this.emitState();
  }

  toggleAtivo(row: MapeamentoCampoRow): void {
    if (this.isEditingLocked) {
      return;
    }

    row.ativo = !row.ativo;
    this.emitState();
  }

  salvar(): void {
    if (this.isEditingLocked) {
      return;
    }

    this.validateAll();

    if (this.hasErrors) {
      const messages: string[] = [];
      for (const row of this.rows) {
        const label = row.nomeCampo?.trim() || `Linha ${this.rows.indexOf(row) + 1}`;
        if (row.errors.nomeCampo) messages.push(`${label}: ${row.errors.nomeCampo}`);
        if (row.errors.indiceColuna) messages.push(`${label}: ${row.errors.indiceColuna}`);
        if (row.errors.tipoCampo) messages.push(`${label}: ${row.errors.tipoCampo}`);
        if (row.errors.formato) messages.push(`${label}: ${row.errors.formato}`);
      }

      this.notificationService.showError(messages.length ? messages.join(' | ') : this.translate.instant('mapeamento.mapeamentoEditor.feedback.errors.fixErrors'));
      this.emitState();
      return;
    }

    if (!this.hasChanges) {
      this.notificationService.showInfo(this.translate.instant('mapeamento.mapeamentoEditor.feedback.info.noChanges'));
      return;
    }

    const payload: AtualizarMapeamentoCamposPayload = {
      campos: this.rows.map((row) => ({
        id: row.id,
        nomeCampo: row.nomeCampo.trim(),
        descricaoCampo: row.descricaoCampo.trim(),
        indiceColuna: row.indiceColuna!,
        tipoCampo: row.tipoCampo as number,
        formato: row.tipoCampo === TipoCampo.DateTime ? (row.formato?.trim() || null) : null,
        ativo: row.ativo
      }))
    };

    this.isSaving = true;
    this.emitState();
    this.mapeamentoService.replaceCampos(this.mapeamento.id, payload)
      .pipe(
        finalize(() => {
          this.isSaving = false;
          this.emitState();
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.notificationService.showSuccess(this.translate.instant('mapeamento.mapeamentoEditor.feedback.success.saved'));
          this.loadCampos(true);
          this.camposChanged.emit();
        },
        error: (error: HttpErrorResponse) => {
          this.notificationService.showError(error.error?.detail ?? this.translate.instant('mapeamento.mapeamentoEditor.feedback.errors.saveFields'));
        }
      });
  }

  cancelar(): void {
    if (this.isEditingLocked) {
      return;
    }

    this.rows = this.originalRows.map((row) => ({ ...row, errors: this.emptyErrors() }));
    this.refreshPreview();
    this.emitState();
  }

  openExcelPreview(): void {
    if (this.isSaving) {
      return;
    }

    this.fileInput?.nativeElement.click();
  }

  baixarPlanilhaExemplo(): void {
    if (this.isDownloadingExample || this.isSaving) {
      return;
    }

    this.isDownloadingExample = true;
    const download = this.managedActions
      ? this.mapeamentoService.downloadPlanilhaExemplo(this.perfilId)
      : this.mapeamentoService.downloadPlanilhaExemploPadrao();

    download
      .pipe(
        finalize(() => { this.isDownloadingExample = false; }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (arquivo) => {
          const url = URL.createObjectURL(arquivo);
          const link = document.createElement('a');
          link.href = url;
          link.download = 'Planilha Exemplo.xlsx';
          link.click();
          URL.revokeObjectURL(url);
        },
        error: async (error: HttpErrorResponse) => {
          let detail: unknown = error.error?.detail;
          if (error.error instanceof Blob && error.error.type.includes('json')) {
            try {
              detail = JSON.parse(await error.error.text())?.detail;
            } catch {
              detail = undefined;
            }
          }

          this.notificationService.showError(
            typeof detail === 'string'
              ? detail
              : this.translate.instant('mapeamento.mapeamentoEditor.feedback.errors.downloadExampleSpreadsheet')
          );
        }
      });
  }

  onExcelUpload(event: Event): void {
    const input = event.target as HTMLInputElement;

    if (this.isSaving) {
      input.value = '';
      return;
    }

    const file = input?.files?.[0];

    if (!file) {
      return;
    }

    this.mapeamentoService.previewExcel(file)
      .pipe(
        finalize(() => {
          input.value = '';
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: ({ colunas }) => {
          if (this.isSaving) {
            return;
          }

          if (colunas.length === 0) {
            this.notificationService.showError(this.translate.instant('mapeamento.mapeamentoEditor.feedback.errors.emptyExcel'));
            return;
          }

          this.excelPreviewData = colunas;
          this.refreshPreview();
          this.notificationService.showSuccess(`${this.translate.instant('mapeamento.mapeamentoEditor.feedback.success.previewLoadedPrefix')} ${this.excelPreviewData.length} ${this.translate.instant('mapeamento.mapeamentoEditor.feedback.success.previewLoadedSuffix')}`);
        },
        error: (error: HttpErrorResponse) => {
          this.notificationService.showError(error.error?.detail ?? this.translate.instant('mapeamento.mapeamentoEditor.feedback.errors.readExcel'));
        }
      });
  }

  isFormatoEnabled(row: MapeamentoCampoRow): boolean {
    return !this.isEditingLocked && row.tipoCampo === TipoCampo.DateTime;
  }

  trackByRow(_: number, row: MapeamentoCampoRow): number | string {
    return row.id ?? `new-${row.nomeCampo}-${row.indiceColuna}`;
  }

  private loadCampos(showSuccess = false): void {
    const requestVersion = ++this.loadRequestVersion;
    const mapeamentoId = this.mapeamento.id;
    this.isLoading = true;
    this.mapeamentoService.getCamposByMapeamento(mapeamentoId)
      .pipe(
        finalize(() => {
          if (requestVersion === this.loadRequestVersion) {
            this.isLoading = false;
          }
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (campos) => {
          if (requestVersion !== this.loadRequestVersion || this.mapeamento.id !== mapeamentoId) {
            return;
          }

          this.rows = campos
            .sort((left, right) => left.indiceColuna - right.indiceColuna)
            .map((campo) => this.toRow(campo));
          this.snapshotOriginal();
          this.refreshPreview();
          this.emitState();

          if (showSuccess) {
            this.notificationService.showSuccess(this.translate.instant('mapeamento.mapeamentoEditor.feedback.success.updated'));
          }
        },
        error: (error: HttpErrorResponse) => {
          if (requestVersion !== this.loadRequestVersion || this.mapeamento.id !== mapeamentoId) {
            return;
          }

          this.rows = [];
          this.originalRows = [];
          this.emitState();
          this.notificationService.showError(error.error?.detail ?? this.translate.instant('mapeamento.mapeamentoEditor.feedback.errors.loadFields'));
        }
      });
  }

  private validateRow(row: MapeamentoCampoRow): void {
    const errors = this.emptyErrors();

    if (!row.nomeCampo?.trim()) {
      errors.nomeCampo = this.translate.instant('mapeamento.mapeamentoEditor.validation.fieldNameRequired');
    }

    if (row.indiceColuna === null || row.indiceColuna === undefined || row.indiceColuna < 1) {
      errors.indiceColuna = this.translate.instant('mapeamento.mapeamentoEditor.validation.columnIndexPositive');
    } else {
      const duplicate = this.rows.find((current) => current !== row && current.indiceColuna === row.indiceColuna);
      if (duplicate) {
        errors.indiceColuna = this.translate.instant('mapeamento.mapeamentoEditor.validation.duplicateColumnIndex');
      }
    }

    if (!row.tipoCampo) {
      errors.tipoCampo = this.translate.instant('mapeamento.mapeamentoEditor.validation.fieldTypeRequired');
    }

    if (row.tipoCampo === TipoCampo.DateTime && !row.formato?.trim()) {
      errors.formato = this.translate.instant('mapeamento.mapeamentoEditor.validation.formatRequired');
    }

    row.errors = errors;
  }

  private validateAll(): void {
    this.rows.forEach((row) => this.validateRow(row));
  }

  private refreshPreview(): void {
    for (const row of this.rows) {
      if (row.indiceColuna !== null && row.indiceColuna > 0 && this.excelPreviewData.length > 0) {
        const index = row.indiceColuna - 1;
        row.previewValue = index < this.excelPreviewData.length
          ? this.excelPreviewData[index]
          : this.translate.instant('mapeamento.mapeamentoEditor.preview.emptyColumn');
      } else {
        row.previewValue = '';
      }
    }
  }

  private toRow(campo: MapeamentoCampo): MapeamentoCampoRow {
    return {
      id: campo.id,
      nomeCampo: campo.nomeCampo,
      descricaoCampo: campo.descricaoCampo ?? '',
      indiceColuna: campo.indiceColuna,
      tipoCampo: campo.tipoCampo,
      formato: campo.formato ?? '',
      ativo: campo.ativo,
      isNew: false,
      previewValue: '',
      errors: this.emptyErrors()
    };
  }

  private snapshotOriginal(): void {
    this.originalRows = this.rows.map((row) => ({ ...row, errors: this.emptyErrors() }));
  }

  private toComparableRow(row: MapeamentoCampoRow): object {
    return {
      id: row.id,
      nomeCampo: row.nomeCampo,
      descricaoCampo: row.descricaoCampo,
      indiceColuna: row.indiceColuna,
      tipoCampo: row.tipoCampo,
      formato: row.formato,
      ativo: row.ativo,
      isNew: row.isNew
    };
  }

  private emitState(): void {
    this.stateChanged.emit({
      hasChanges: this.hasChanges,
      hasErrors: this.hasErrors,
      isSaving: this.isSaving
    });
  }

  private emptyErrors(): MapeamentoRowErrors {
    return { nomeCampo: '', indiceColuna: '', tipoCampo: '', formato: '' };
  }
}

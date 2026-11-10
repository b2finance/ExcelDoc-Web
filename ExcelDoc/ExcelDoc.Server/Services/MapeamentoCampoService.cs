using ExcelDoc.Server.DTOs.Mapeamentos;
using ExcelDoc.Server.Localization;
using ExcelDoc.Server.Models;
using ExcelDoc.Server.Repositories.Interfaces;
using ExcelDoc.Server.Services.Interfaces;

namespace ExcelDoc.Server.Services
{
    public class MapeamentoCampoService : IMapeamentoCampoService
    {
        private readonly IMapeamentoRepository _mapeamentoRepository;
        private readonly IMessageService _messageService;
        private readonly IUsuarioAcessoService _usuarioAcessoService;

        public MapeamentoCampoService(
            IMapeamentoRepository mapeamentoRepository,
            IMessageService messageService,
            IUsuarioAcessoService usuarioAcessoService)
        {
            _mapeamentoRepository = mapeamentoRepository;
            _messageService = messageService;
            _usuarioAcessoService = usuarioAcessoService;
        }

        public async Task<IReadOnlyCollection<MapeamentoCampoResponseDto>> GetByMapeamentoAsync(int mapeamentoId, CancellationToken cancellationToken = default)
        {
            await _usuarioAcessoService.GetUsuarioAtualAsync(cancellationToken);
            var mapeamento = await _mapeamentoRepository.GetMapeamentoByIdAsync(mapeamentoId, cancellationToken)
                ?? throw new KeyNotFoundException(_messageService.Get(MessageKeys.MappingNotFound));

            var campos = await _mapeamentoRepository.GetCamposByMapeamentoIdAsync(mapeamentoId, cancellationToken);
            return campos.Select(Map).ToList();
        }

        public async Task<MapeamentoCampoResponseDto> CriarAsync(MapeamentoCampoRequestDto request, CancellationToken cancellationToken = default)
        {
            var usuario = await _usuarioAcessoService.GetUsuarioAtualAsync(cancellationToken);
            var mapeamento = await _mapeamentoRepository.GetMapeamentoByIdAsync(request.FK_IdMapeamento, cancellationToken)
                ?? throw new KeyNotFoundException(_messageService.Get(MessageKeys.MappingNotFound));

            EnsureCanEditMapeamento(usuario, mapeamento);
            await ValidateCampoAsync(request, null, cancellationToken);

            var campo = new MapeamentoCampo
            {
                NomeCampo = request.NomeCampo.Trim(),
                DescricaoCampo = request.DescricaoCampo?.Trim() ?? string.Empty,
                IndiceColuna = request.IndiceColuna,
                TipoCampo = request.TipoCampo,
                Formato = request.TipoCampo == TipoCampo.DateTime ? request.Formato?.Trim() : null,
                Ativo = request.Ativo,
                FK_IdMapeamento = request.FK_IdMapeamento
            };

            await _mapeamentoRepository.AddCampoAsync(campo, cancellationToken);
            await _mapeamentoRepository.SaveChangesAsync(cancellationToken);

            return Map(campo);
        }

        public async Task<MapeamentoCampoResponseDto> AtualizarAsync(int id, MapeamentoCampoRequestDto request, CancellationToken cancellationToken = default)
        {
            var usuario = await _usuarioAcessoService.GetUsuarioAtualAsync(cancellationToken);
            var campo = await _mapeamentoRepository.GetCampoByIdAsync(id, cancellationToken)
                ?? throw new KeyNotFoundException(_messageService.Get(MessageKeys.MappingFieldNotFound));

            EnsureCanEditMapeamento(usuario, campo.Mapeamento);

            if (campo.FK_IdMapeamento != request.FK_IdMapeamento)
            {
                throw new InvalidOperationException(_messageService.Get(MessageKeys.MappingFieldMappingCannotBeChanged));
            }

            await ValidateCampoAsync(request, id, cancellationToken);

            campo.NomeCampo = request.NomeCampo.Trim();
            campo.DescricaoCampo = request.DescricaoCampo?.Trim() ?? campo.DescricaoCampo;
            campo.IndiceColuna = request.IndiceColuna;
            campo.TipoCampo = request.TipoCampo;
            campo.Formato = request.TipoCampo == TipoCampo.DateTime ? request.Formato?.Trim() : null;
            campo.Ativo = request.Ativo;

            await _mapeamentoRepository.SaveChangesAsync(cancellationToken);
            return Map(campo);
        }

        public async Task<IReadOnlyCollection<MapeamentoCampoResponseDto>> SubstituirAsync(
            int mapeamentoId,
            AtualizarMapeamentoCamposRequestDto request,
            CancellationToken cancellationToken = default)
        {
            var usuario = await _usuarioAcessoService.GetUsuarioAtualAsync(cancellationToken);
            var mapeamento = await _mapeamentoRepository.GetMapeamentoByIdAsync(mapeamentoId, cancellationToken)
                ?? throw new KeyNotFoundException(_messageService.Get(MessageKeys.MappingNotFound));

            EnsureCanEditMapeamento(usuario, mapeamento);

            var indices = request.Campos.Select(campo => campo.IndiceColuna).ToList();
            if (indices.Distinct().Count() != indices.Count)
            {
                var indiceDuplicado = indices
                    .GroupBy(indice => indice)
                    .First(grupo => grupo.Count() > 1)
                    .Key;

                throw new InvalidOperationException(
                    _messageService.Get(MessageKeys.MappingFieldColumnIndexAlreadyExists, indiceDuplicado));
            }

            var idsInformados = request.Campos
                .Where(campo => campo.Id.HasValue)
                .Select(campo => campo.Id!.Value)
                .ToList();

            if (idsInformados.Distinct().Count() != idsInformados.Count)
            {
                throw new InvalidOperationException(_messageService.Get(MessageKeys.MappingFieldMappingCannotBeChanged));
            }

            var camposExistentesPorId = mapeamento.Campos.ToDictionary(campo => campo.Id);
            if (idsInformados.Any(id => !camposExistentesPorId.ContainsKey(id)))
            {
                throw new KeyNotFoundException(_messageService.Get(MessageKeys.MappingFieldNotFound));
            }

            var camposDesejados = request.Campos.Select(campoRequest =>
            {
                if (!Enum.IsDefined(typeof(TipoCampo), campoRequest.TipoCampo))
                {
                    throw new InvalidOperationException(_messageService.Get(MessageKeys.FieldTypeRequired));
                }

                if (campoRequest.TipoCampo == TipoCampo.DateTime && string.IsNullOrWhiteSpace(campoRequest.Formato))
                {
                    throw new InvalidOperationException(_messageService.Get(MessageKeys.DateTimeFieldFormatRequired));
                }

                var descricao = campoRequest.DescricaoCampo?.Trim()
                    ?? (campoRequest.Id.HasValue ? camposExistentesPorId[campoRequest.Id.Value].DescricaoCampo : string.Empty);

                return new MapeamentoCampo
                {
                    Id = campoRequest.Id ?? 0,
                    NomeCampo = campoRequest.NomeCampo.Trim(),
                    DescricaoCampo = descricao,
                    IndiceColuna = campoRequest.IndiceColuna,
                    TipoCampo = campoRequest.TipoCampo,
                    Formato = campoRequest.TipoCampo == TipoCampo.DateTime ? campoRequest.Formato?.Trim() : null,
                    Ativo = campoRequest.Ativo,
                    FK_IdMapeamento = mapeamento.Id
                };
            }).ToList();

            await _mapeamentoRepository.ReplaceCamposAsync(mapeamento, camposDesejados, cancellationToken);

            return mapeamento.Campos
                .OrderBy(campo => campo.IndiceColuna)
                .Select(Map)
                .ToList();
        }

        public async Task ExcluirAsync(int id, CancellationToken cancellationToken = default)
        {
            var usuario = await _usuarioAcessoService.GetUsuarioAtualAsync(cancellationToken);
            var campo = await _mapeamentoRepository.GetCampoByIdAsync(id, cancellationToken)
                ?? throw new KeyNotFoundException(_messageService.Get(MessageKeys.MappingFieldNotFound));

            EnsureCanEditMapeamento(usuario, campo.Mapeamento);
            _mapeamentoRepository.RemoveCampo(campo);
            await _mapeamentoRepository.SaveChangesAsync(cancellationToken);
        }

        private async Task ValidateCampoAsync(MapeamentoCampoRequestDto request, int? ignoreId, CancellationToken cancellationToken)
        {
            if (request.TipoCampo == TipoCampo.DateTime && string.IsNullOrWhiteSpace(request.Formato))
            {
                throw new InvalidOperationException(_messageService.Get(MessageKeys.DateTimeFieldFormatRequired));
            }

            if (await _mapeamentoRepository.ExistsIndiceNoMapeamentoAsync(request.FK_IdMapeamento, request.IndiceColuna, ignoreId, cancellationToken))
            {
                throw new InvalidOperationException(_messageService.Get(MessageKeys.MappingFieldColumnIndexAlreadyExists, request.IndiceColuna));
            }
        }

        private void EnsureCanEditMapeamento(Usuario usuario, Mapeamento mapeamento)
        {
            if (usuario.TipoUsuario == TipoUsuario.Administrador)
            {
                return;
            }

            if (mapeamento.IsPadraoGlobal)
            {
                throw new UnauthorizedAccessException(_messageService.Get(MessageKeys.UserDoesNotHavePermissionToChangeMapping));
            }
        }

        private static MapeamentoCampoResponseDto Map(MapeamentoCampo campo)
        {
            return new MapeamentoCampoResponseDto
            {
                Id = campo.Id,
                NomeCampo = campo.NomeCampo,
                DescricaoCampo = campo.DescricaoCampo,
                IndiceColuna = campo.IndiceColuna,
                TipoCampo = campo.TipoCampo,
                Formato = campo.Formato,
                Ativo = campo.Ativo
            };
        }
    }
}

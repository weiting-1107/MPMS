using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace MPMS.ModelBinders
{
    public class TrimmingModelBinder : IModelBinder
    {
        private readonly IModelBinder _fallbackBinder;

        public TrimmingModelBinder(IModelBinder fallbackBinder)
        {
            _fallbackBinder = fallbackBinder ?? throw new ArgumentNullException(nameof(fallbackBinder));
        }

        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null) throw new ArgumentNullException(nameof(bindingContext));

            var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            if (valueProviderResult != ValueProviderResult.None)
            {
                var value = valueProviderResult.FirstValue;
                if (value != null)
                {
                    bindingContext.Result = ModelBindingResult.Success(value.Trim());
                    return;
                }
            }

            await _fallbackBinder.BindModelAsync(bindingContext);
            if (bindingContext.Result.IsModelSet && bindingContext.Result.Model is string val)
            {
                bindingContext.Result = ModelBindingResult.Success(val.Trim());
            }
        }
    }

    public class TrimmingModelBinderProvider : IModelBinderProvider
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (context.Metadata.ModelType == typeof(string))
            {
                var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
                var fallbackBinder = new SimpleTypeModelBinder(context.Metadata.ModelType, loggerFactory);
                return new TrimmingModelBinder(fallbackBinder);
            }

            return null;
        }
    }
}

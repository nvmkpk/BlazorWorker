﻿using Microsoft.JSInterop;
using MonoWorker.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Map = System.Collections.Generic.Dictionary<string, string>;
namespace BlazorWorker.Core
{
    [DependencyHint(typeof(MessageService))]
    
    public class WorkerProxy : IWorker
    {
        private static readonly IReadOnlyDictionary<string, string> escapeScriptTextReplacements =
            new Dictionary<string, string> { { @"\", @"\\" }, { "\r", @"\r" }, { "\n", @"\n" }, { "'", @"\'" }, { "\"", @"\""" } };
        private readonly IJSRuntime jsRuntime;
        private readonly ScriptLoader scriptLoader;
        private static long idSource;
        private bool isDisposed = false;
        /// <summary>
        /// [MonoWorker.Core]MonoWorker.Core.MessageService:OnMessage"
        /// </summary>
        private static readonly string messageMethod;

        public event EventHandler<string> IncomingMessage;
        public bool IsInitialized { get; private set; }
        static WorkerProxy()
        {
            var messageServiceType = typeof(MessageService);
            messageMethod = $"[{messageServiceType.Assembly.GetName().Name}]{messageServiceType.FullName}:{nameof(MessageService.OnMessage)}";
        }

        public WorkerProxy(IJSRuntime jsRuntime)
        {
            this.jsRuntime = jsRuntime;
            this.scriptLoader = new ScriptLoader(this.jsRuntime);
            this.Identifier = ++idSource;
        }

        public async ValueTask DisposeAsync()
        {
            if (!isDisposed)
    
            {
                await this.jsRuntime.InvokeVoidAsync("BlazorWorker.disposeWorker", this.Identifier);
                isDisposed = true;
            }
        }

        public async Task InitAsync(WorkerInitOptions initOptions)
        {
            await this.scriptLoader.InitScript();
            var embeddedReferences = new Map
            {
                { "WebAssembly.Bindings.dll", "BlazorWorker.Core.WebAssembly.Bindings.0.2.2.0.dll" },
               // { "WebAssembly.Net.Http.dll", "BlazorWorker.Core.WebAssembly.Net.Http.dll" }
            };

            var fetchResponses = embeddedReferences.Select(x => embeddedReferences[x.Key]).ToDictionary(x => x, resourceName => {
                byte[] dllContent;
                var stream = this.GetType().Assembly.GetManifestResourceStream(resourceName);
                using (stream)
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    dllContent = ms.ToArray();
                }
                return new FetchResponse() { Url = resourceName, Base64Data = Convert.ToBase64String(dllContent) };
            });
            

            await this.jsRuntime.InvokeVoidAsync(
                "BlazorWorker.initWorker", 
                this.Identifier, 
                DotNetObjectReference.Create(this), 
                new WorkerInitOptions {
                    DependentAssemblyFilenames = 
                        new[] { 
                            "MonoWorker.Core.dll", 
                            "netstandard.dll",
                            "mscorlib.dll",
                            "WebAssembly.Bindings.dll",
                            "System.dll",
                            "System.Core.dll",
                            /*
                             "System.Net.Http.dll",
                            "System.Memory.dll",
                            "System.Numerics.dll",
                            "System.Numerics.Vectors.dll",
                            "System.Runtime.CompilerServices.Unsafe.dll",
                            "System.Runtime.Serialization.dll",
                            "WebAssembly.Net.Http.dll",
                            "Mono.Security.dll",
                            "System.ServiceModel.Internals.dll"*/
                        },
                    FetchUrlOverride = embeddedReferences,
                    FetchOverride = fetchResponses,
                    CallbackMethod = nameof(OnMessage),
                    MessageEndPoint = messageMethod //"[MonoWorker.Core]MonoWorker.Core.MessageService:OnMessage"
               }.MergeWith(initOptions));
        }

        [JSInvokable]
        public async Task OnMessage(string message)
        {
            IncomingMessage?.Invoke(this, message);
        }

        public async Task PostMessageAsync(string message)
        {
            await jsRuntime.InvokeVoidAsync("BlazorWorker.postMessage", this.Identifier, message);
        }

        public long Identifier { get; }
    }
}

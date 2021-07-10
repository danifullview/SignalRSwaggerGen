﻿using Microsoft.OpenApi.Models;
using SignalRSwaggerGen.Attributes;
using SignalRSwaggerGen.Enums;
using SignalRSwaggerGen.Utils;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SignalRSwaggerGen
{
	/// <summary>
	/// This class can be used by Swagger to generate documentation for SignalR hubs.
	/// In order for Swagger to use this class, just add this class as document filter for Swagger generator.
	/// Don't forget to add the list of assemblies which contain SignalR hubs as parameter for document filter.
	/// </summary>
	public sealed class SignalRSwaggerGen : IDocumentFilter
	{
		private IEnumerable<Assembly> Assemblies { get; }

		/// <param name="assemblies">Assemblies which contain SignalR hubs</param>
		/// <exception cref="ArgumentException">Thrown if no assemblies provided</exception>
		public SignalRSwaggerGen(List<Assembly> assemblies)
		{
			if (assemblies == null || !assemblies.Any()) throw new ArgumentException("No assemblies provided", nameof(assemblies));
			Assemblies = assemblies.Distinct();
		}

		/// <summary>
		/// This method is automatically called by Swagger generator
		/// </summary>
		/// <param name="swaggerDoc"></param>
		/// <param name="context"></param>
		public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
		{
			var hubs = GetHubs();
			foreach (var hub in hubs)
			{
				ProcessHub(swaggerDoc, context, hub);
			}
		}

		private static void ProcessHub(OpenApiDocument swaggerDoc, DocumentFilterContext context, Type hub)
		{
			var hubAttribute = hub.GetCustomAttribute<SignalRHubAttribute>();
			if (!ShouldBeDisplayedOnDocument(context, hubAttribute)) return;
			var hubPath = GetHubPath(hub, hubAttribute);
			var tag = GetTag(hub);
			var methods = GetHubMethods(hub, hubAttribute);
			foreach (var method in methods)
			{
				ProcessMethod(swaggerDoc, context, hubAttribute, hubPath, tag, method);
			}
		}

		private static void ProcessMethod(
			OpenApiDocument swaggerDoc,
			DocumentFilterContext context,
			SignalRHubAttribute hubAttribute,
			string hubPath,
			string tag,
			MethodInfo method)
		{
			var methodAttribute = method.GetCustomAttribute<SignalRMethodAttribute>();
			var methodPath = GetMethodPath(hubPath, method, methodAttribute);
			var methodArgs = GetMethodArgs(method, hubAttribute, methodAttribute);
			var operationType = methodAttribute?.OperationType ?? Constants.DefaultOperationType;
			var summary = methodAttribute?.Summary;
			var description = methodAttribute?.Description;
			AddOpenApiPath(swaggerDoc, context, tag, methodPath, operationType, summary, description, methodArgs);
		}

		private static void AddOpenApiPath(
			OpenApiDocument swaggerDoc,
			DocumentFilterContext context,
			string tag,
			string methodPath,
			OperationType operationType,
			string summary,
			string description,
			IEnumerable<ParameterInfo> methodArgs)
		{
			swaggerDoc.Paths.Add(
				methodPath,
				new OpenApiPathItem
				{
					Operations = new Dictionary<OperationType, OpenApiOperation>
					{
						{
							operationType,
							new OpenApiOperation
							{
								Summary = summary,
								Description = description,
								Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
								Parameters = ToOpenApiParameters(context, methodArgs).ToList()
							}
						}
					}
				});
		}

		private static IEnumerable<OpenApiParameter> ToOpenApiParameters(DocumentFilterContext context, IEnumerable<ParameterInfo> args)
		{
			return args.Select(arg =>
			{
				var argAttribute = arg.GetCustomAttribute<SignalRArgAttribute>();
				var description = argAttribute?.Description;
				var param = new OpenApiParameter
				{
					Name = arg.Name,
					In = ParameterLocation.Query,
					Description = description
				};
				var schema = GetOpenApiSchema(context, arg.ParameterType);
				param.Schema = schema.Reference == null
					? schema
					: new OpenApiSchema
					{
						Reference = new OpenApiReference
						{
							Id = schema.Reference.Id,
							Type = ReferenceType.Schema
						}
					};
				return param;
			});
		}

		private static OpenApiSchema GetOpenApiSchema(DocumentFilterContext context, Type type)
		{
			if (context.SchemaRepository.TryLookupByType(type, out OpenApiSchema schema)) return schema;
			return context.SchemaGenerator.GenerateSchema(type, context.SchemaRepository);
		}

		private static bool ShouldBeDisplayedOnDocument(DocumentFilterContext context, SignalRHubAttribute hubAttribute)
		{
			return hubAttribute.DocumentNames == null
				|| !hubAttribute.DocumentNames.Any()
				|| hubAttribute.DocumentNames.Contains(context.DocumentName);
		}

		private static string GetHubPath(Type hub, SignalRHubAttribute hubAttribute)
		{
			return hubAttribute.Path.Replace(Constants.HubNamePlaceholder, GetTypeName(hub));
		}

		private static string GetMethodPath(string hubPath, MethodInfo method, SignalRMethodAttribute methodAttribute)
		{
			return methodAttribute == null
				? $"{hubPath}/{method.Name}"
				: $"{hubPath}/{methodAttribute.Name.Replace(Constants.MethodNamePlaceholder, method.Name)}";
		}

		private static string GetTag(Type hub)
		{
			return GetTypeName(hub);
		}

		private IEnumerable<Type> GetHubs()
		{
			return Assemblies
				.SelectMany(a =>
					a.GetTypes()
					.Where(t =>
						t.GetCustomAttribute<SignalRHubAttribute>() != null
						&& t.GetCustomAttribute<SignalRHiddenAttribute>() == null));
		}

		private static IEnumerable<MethodInfo> GetHubMethods(Type hub, SignalRHubAttribute hubAttribute)
		{
			IEnumerable<MethodInfo> methods;
			switch (hubAttribute.AutoDiscover)
			{
				case AutoDiscover.None:
					methods = hub.GetMethods(ReflectionUtils.DeclaredPublicInstance).Where(x => x.GetCustomAttribute<SignalRMethodAttribute>() != null);
					break;
				case AutoDiscover.Methods:
				case AutoDiscover.MethodsAndArgs:
					methods = hub.GetMethods(ReflectionUtils.DeclaredPublicInstance);
					break;
				default:
					throw new NotSupportedException($"Value {hubAttribute.AutoDiscover} not supported");
			}
			return methods.Where(x => x.GetCustomAttribute<SignalRHiddenAttribute>() == null);
		}

		private static IEnumerable<ParameterInfo> GetMethodArgs(MethodInfo method, SignalRHubAttribute hubAttribute, SignalRMethodAttribute methodAttribute)
		{
			IEnumerable<ParameterInfo> methodArgs;
			if (methodAttribute == null)
			{
				switch (hubAttribute.AutoDiscover)
				{
					case AutoDiscover.None:
					case AutoDiscover.Methods:
						methodArgs = method.GetParameters().Where(x => x.GetCustomAttribute<SignalRArgAttribute>() != null);
						break;
					case AutoDiscover.MethodsAndArgs:
						methodArgs = method.GetParameters();
						break;
					default:
						throw new NotSupportedException($"Value {hubAttribute.AutoDiscover} not supported");
				}
			}
			else
			{
				switch (methodAttribute.AutoDiscover)
				{
					case AutoDiscover.None:
						methodArgs = method.GetParameters().Where(x => x.GetCustomAttribute<SignalRArgAttribute>() != null);
						break;
					case AutoDiscover.Args:
						methodArgs = method.GetParameters();
						break;
					default:
						throw new NotSupportedException($"Value {hubAttribute.AutoDiscover} not supported");
				}
			}
			return methodArgs.Where(x => x.GetCustomAttribute<SignalRHiddenAttribute>() == null);
		}

		private static string GetTypeName(Type type)
		{
			if (type.IsInterface && type.Name[0] == 'I') return type.Name[1..];
			return type.Name;
		}
	}
}

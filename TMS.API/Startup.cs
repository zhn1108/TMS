using Autofac;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TMS.Common.DB;
using TMS.Common.Filter;
using Serilog.Extensions.Logging.File;
using Microsoft.IdentityModel.Tokens;
using TMS.Common.JWT;
using Swashbuckle.AspNetCore.Filters;
using TMS.Common.Redis;

namespace TMS.API
{
    /// <summary>
    /// 
    /// </summary>
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }
        //public void ConfigureContainer(ContainerBuilder build)
        //{
        //    var bllFilePath = System.IO.Path.Combine(AppContext.BaseDirectory, "TMS.Repository.dll"); //DDal.dll������ע��Ĳ�
        //    build.RegisterAssemblyTypes(Assembly.LoadFile(bllFilePath)).AsImplementedInterfaces();
        //}

        //This method gets called by the runtime.Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            
             #region JWT����
              services.AddAuthentication(options => {
                     //��֤middleware����
                   options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                     options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                 }).AddJwtBearer(options => {
                      //��Ҫ��jwt  token��������
                 options.TokenValidationParameters = new TokenValidationParameters {
                     //�䷢��
                     ValidateIssuer = true,
                     ValidIssuer = Configuration["JwtSetting:Issuer"],
                     //����Ȩ��
                     ValidateAudience = true,
                     ValidAudience = Configuration["JwtSetting:Audience"],
                     //��Կ�����ܣ�
                     ValidateIssuerSigningKey = true,
                     IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["JwtSetting:SecretKey"])),
                     //�Ƿ���֤ʧЧʱ�䡾ʹ�õ�ǰʱ����Token��Claims��NotBefore��Expires�Աȡ�
                     ValidateLifetime = true,
                     ClockSkew = TimeSpan.FromMinutes(5)//����ķ�����ʱ��ƫ����5���ӡ�
                 };
                  });

            #endregion  ConfigureServices����

            #region ע��jwt�м��
             services.AddTransient<JWTService>(); 
            #endregion


            #region SQLע��
            //�������ϼ�SQLע�������
            //services.AddControllers(options =>
            //{
            //    options.Filters.Add<CustomSQLInjectFilter>();
            //});
            services.AddControllers();
            #endregion

            #region ע��Swagger����
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "TMS.API", Version = "v1" });
                // Ϊ Swagger ����xml�ĵ�ע��·��
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                // ��ӿ�������ע�ͣ�true��ʾ��ʾ������ע��
                c.IncludeXmlComments(xmlPath, true);

                #region swagger��JWT��֤
                //����Ȩ��С��
                c.OperationFilter<AddResponseHeadersFilter>();
                c.OperationFilter<AppendAuthorizeToSummaryOperationFilter>();
                //��header�����token�����ݵ���̨
                c.OperationFilter<SecurityRequirementsOperationFilter>();
                c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
                {
                    Description = "JWT��Ȩ(���ݽ�������ͷ�н��д���)ֱ���������������Bearer {token}(ע������֮����һ���ո�) \"",
                    Name = "Authorization",// tĬ�ϵĲ�������
                    In = ParameterLocation.Header,// tĬ�ϴ��Authorization��Ϣ��λ��(����ͷ��)
                    Type = SecuritySchemeType.ApiKey
                });
                #endregion

            });
            #endregion

            #region ����
            //���cors ���� ���ÿ���������
            services.AddCors(c => {
                c.AddPolicy("AllRequests", policy => {
                    policy
           .WithOrigins("http://192.168.31.231:8080", "http://192.168.31.231:8081")
           .AllowAnyMethod()
           .AllowAnyHeader();

                    

                });

            }    
            );
            #endregion


            #region ���ݿ�����
            DbFactory.DbConString = Configuration.GetConnectionString("MySql");
            #endregion

            #region  Redis
            //redis����
            var section = Configuration.GetSection("Redis:Default");
            //�����ַ���
            //string _connectionString = section.GetSection("Connection").Value;
            string _connectionString = DbFactory.DbConString;
            //ʵ������
            string _instanceName = section.GetSection("InstanceName").Value;
            //Ĭ�����ݿ� 
            int _defaultDB = int.Parse(section.GetSection("DefaultDB").Value ?? "0");
            services.AddSingleton(new RedisHelper(_connectionString, _instanceName, _defaultDB));
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
            #endregion

            services.AddControllers(options =>
            {
                options.Filters.Add(new CustomerExceptionFilter());
            });

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env,ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();//�쳣�������ڿ���ģʽ��һ���������ת�������ջҳ��
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "TMS.API v1"));
            }

            app.UseCors("AllRequests");

            #region ����JWT(�����֤�м������������Ȩǰ����ӡ�)
            app.UseAuthentication();
            #endregion

            #region ��Ȩ�м��
            app.UseAuthorization();
            #endregion

            #region  �ܵ��쳣����
            app.UseExceptionMiddleware();
            #endregion

            #region Nlog
            //AddFile()������Serilog.Extensions.Logging.File���ṩ����չ���������ڽ���־д���ļ���
            loggerFactory.AddFile("Logs/log{Date}.txt");
            #endregion

         


            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }


        /// <summary>
        /// �Զ�����ע��
        /// </summary>
        /// <param name="builder"></param>
        public void ConfigureContainer(ContainerBuilder builder)
        {
            string bllFilePath = Path.Combine(AppContext.BaseDirectory, "TMS.Repository.dll");
            builder.RegisterAssemblyTypes(Assembly.LoadFile(bllFilePath)).AsImplementedInterfaces();
        }




    }
}

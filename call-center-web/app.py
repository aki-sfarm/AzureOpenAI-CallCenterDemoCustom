'''
Component:  Contact Center AI Demo 
Purpose:    This is a demo solution showcasing AI applied in contact center solutions with the power of GPT
Copyright (c) Microsoft Corporation
Licensed under the MIT license.
'''
import os
import json
import logging
import re
import datetime
from datetime import datetime as dt, timedelta
import base64
import uuid

from flask import Flask, render_template, request
from flask_cors import CORS
from flask_session import Session  # https://pythonhosted.org/Flask-Session



import openai
from azure.ai.textanalytics import TextAnalyticsClient
from azure.identity import DefaultAzureCredential
from werkzeug.middleware.proxy_fix import ProxyFix


from utilities import config

from azure.storage.queue import QueueClient

from azure.identity import DefaultAzureCredential
from azure.cosmos import CosmosClient

app = Flask(__name__)
app.secret_key = config.APP_SECRET
app.config['JWT_SECRET_KEY'] = config.JWT_SECRET
app.config['MAX_CONTENT_LENGTH'] = config.MAX_CONTENT_SIZE * 1024*1024
CORS(app)
Session(app)

# set logging level
logging.basicConfig(level=logging.WARNING)

# Text Analytics client global instance
text_analytics_client = None

# For generating https scheme when deployed behind reverse proxy
app.wsgi_app = ProxyFix(app.wsgi_app, x_proto=1, x_host=1)

@app.route('/', methods=['GET'])
def callcenter():
    
    try:
        # Text Analytics client global instance
        endpoint = os.environ["TEXT_ANALYTICS_ENDPOINT"]
        global text_analytics_client
        text_analytics_client = TextAnalyticsClient(endpoint, credential=DefaultAzureCredential())
    except Exception as e:
        print(e.args)
        return "TEXT_ANALYTICS_ENDPOINT enviroment variable is not set correctly."
    
    return render_template('call_center_ai.html')

@app.route('/token', methods=['POST'])
def token():
    try:
        azure_token = DefaultAzureCredential().get_token("https://cognitiveservices.azure.com/.default")
        resource_id = os.environ.get('STT_RESOURCE_ID')
        speech_region = os.environ.get('STT_REGION')

        return json.dumps({
            'status': 'OK',
            'azure_token': azure_token.token,
            'resource_id': resource_id,
            'speech_region': speech_region
        })
    
    except Exception as e:
        print(e.args)
        return json.dumps({
            'status': 'Error',
            'error': 'Failed to get acess token, check Azure App Service - Managed ID settings.'
        })

@app.route('/sentiment', methods=['POST'])
def sentiment():
    try:
        data = request.json
    
        result = text_analytics_client.analyze_sentiment(documents=data['documents'])
        confidence_scores = result[0].confidence_scores

        return json.dumps({
            'status': 'OK',
            'confidence_scores': [{
                'negative': confidence_scores.negative,
                'neutral': confidence_scores.neutral,
                'positive': confidence_scores.positive,
            }]
        })
    
    except Exception as e:
        print(e.args)
        return json.dumps({
            'status': 'Error',
            'error': e.args
        })

@app.route('/gpt', methods=['POST'])
def gpt():
    try:
        data = request.json
        transcription = data['transcription']

        # Get environment variables
        aoaiEndpoint = os.environ.get('AOAI_ENDPOINT')
        gptModel = os.environ.get('AOAI_MODEL')
        env_queue_name = os.environ.get("QUEUE_NAME")
        env_account_url = os.environ.get("Queue_ACCOUNT_URL")

        # Call Azure OpenAI text-davinci-003 model
        openai.api_type = 'azure_ad'
        openai.api_version = '2023-03-15-preview'
        openai.api_base =  aoaiEndpoint
        azure_token = DefaultAzureCredential().get_token("https://cognitiveservices.azure.com/.default")
        openai.api_key = azure_token.token
        
        system_content = '\n"""\n会話内容を、次の4項目について回答しなさい。回答は A1>, A2>, A3>, A4> として記述し各項目の説明は不要です:\nQ1>. 簡潔な文章に要約:\nQ2>. センチメントスコアを0～100の数値で評価し、数値で回答:\nQ3>. キーエンティティを抽出:\nQ4>. 該当するカテゴリはどれか [情報,質問,技術,クレーム,依頼,その他]\n"""'
        user_content=transcription[0:2600]

        response = openai.ChatCompletion.create(engine=gptModel, 
                                            messages=[
                                                {"role": "system", "content": system_content},
                                                {"role": "user", "content": user_content}
                                            ],
                                            temperature=0, 
                                            max_tokens=400)
        text = response['choices'][0]['message']['content'].replace('\n', '').replace(' .', '.').strip()

        # regex function to extract all characters after [4].
        def extract_category(pattern, text):
            matches = re.findall(pattern, text)[0]
            return matches[1].replace('.', '').replace('>','').strip()

        pattern1 = r'(Q1|A1)(.*?)(Q2|A2)'
        pattern2 = r'(A2|Q2)(.*?)(A3|Q3)'
        pattern3 = r'(Q3|A3)(.*?)(Q4|A4)'
        pattern4 = r"(Q4|A4)(.*)"
        

        current_utc_time = dt.utcnow()
        # UTCをJSTに変換 (+9時間)
        current_jst_time = current_utc_time + timedelta(hours=9)
        

        
        ToCosmosDb_json = {
            "dateTime": current_jst_time.strftime('%Y-%m-%dT%H:%M:%SZ'),
            "summary": extract_category(pattern1, text).strip('.'),
            "sentimentScore": float(extract_category(pattern2, text).replace('.', '')), 
            "keyEntities": [entity.strip() for entity in extract_category(pattern3, text).split('、')],
            "topic": [topic.strip() for topic in extract_category(pattern4, text).split('、')],
            "CallLog": user_content
        }
        
       
        
        credential = DefaultAzureCredential()
        queue_client = QueueClient(account_url=env_account_url, 
                           queue_name=env_queue_name, 
                           credential=credential)
        encoded_message = base64.b64encode(json.dumps(ToCosmosDb_json).encode('utf-8')).decode('utf-8')  # 2. メッセージをBase-64エンコード

        queue_client.send_message(encoded_message) 
        
        #queue_client.send_message(json.dumps(ToCosmosDb_json, ensure_ascii=False))
        #queue_client.send_message(json.dumps(ToCosmosDb_json))

        # CosmosDBのエンドポイントとデータベース、コンテナの情報
        #COSMOSDB_URL = ""
        #COSMOSDB_KEY = ""
        #DATABASE_NAME = ''
        #CONTAINER_NAME = ''
        
        #client = CosmosClient(COSMOSDB_URL, credential=COSMOSDB_KEY)
        #database = client.get_database_client(DATABASE_NAME)
        #container = database.get_container_client(CONTAINER_NAME)
        
        #container.upsert_item(ToCosmosDb_json)


        return json.dumps({
            'status': 'OK', 
            'q1': extract_category(pattern1, text).strip('.'), 
            'q2': extract_category(pattern2, text).replace('.', ''), 
            'q3': extract_category(pattern3, text).strip('.').split(','), 
            'q4': extract_category(pattern4, text).strip('.')
        })
    
    except Exception as e:
        print(e.args)
        return json.dumps({'error': e.args})